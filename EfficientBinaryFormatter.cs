using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;

namespace NMaier.Serialization
{
  /// <summary>
  ///   A somewhat efficient binary formatter for serialization
  /// </summary>
  public sealed class EfficientBinaryFormatter : IFormatter
  {
    private static readonly IFormatterConverter converter = new FormatterConverter();
    private static readonly SerializationBinder defaultBinder = new EfficientSerializationBinder();

    private static readonly ConcurrentDictionary<Type, ConstructorInfo> deserials =
      new ConcurrentDictionary<Type, ConstructorInfo>();

    private static readonly ConcurrentDictionary<MemberInfo, bool> optionals =
      new ConcurrentDictionary<MemberInfo, bool>();

    private static readonly ConcurrentDictionary<Type, MemberInfo[]> serializable =
      new ConcurrentDictionary<Type, MemberInfo[]>();

    private static ConstructorInfo GetDeserializationConstructor(Type type)
    {
      return deserials.GetOrAdd(type, t =>
      {
        var ctor = t.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                                    new[] {typeof (SerializationInfo), typeof (StreamingContext)}, null);
        if (ctor == null) {
          throw new SerializationException($"Cannot find deserialization constructor for {t.FullName}");
        }
        return ctor;
      });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsOptional(MemberInfo member)
    {
      return optionals.GetOrAdd(member, m => m.GetCustomAttributes(typeof (OptionalFieldAttribute), false).Length > 0);
    }

    private static void VisitElements(Array array, Action<Array, int[]> action)
    {
      if (array == null) {
        throw new ArgumentNullException(nameof(array));
      }
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }

      var dimensions = array.Rank;

      var indices = new int[dimensions];

      for (var i = 0; i < indices.Length; i++) {
        var lowerBound = array.GetLowerBound(i);
        var upperBound = array.GetUpperBound(i);

        if (lowerBound > upperBound) {
          return;
        }
        indices[i] = lowerBound;
      }

      bool exists;
      do {
        action(array, indices);
        exists = false;
        for (var d = 0; d < dimensions; d++) {
          var idx = indices[d];

          var ub = array.GetUpperBound(d);
          if (++idx > ub) {
            indices[d] = array.GetLowerBound(d);
            continue;
          }
          indices[d] = idx;
          exists = true;
          break;
        }
      } while (exists);
    }

    /// <summary>
    ///   Construct with default StreamingContext
    /// </summary>
    public EfficientBinaryFormatter()
    {
      Context = new StreamingContext();
    }

    /// <summary>
    ///   Construct with a custom StreamingContext
    /// </summary>
    /// <param name="context"></param>
    public EfficientBinaryFormatter(StreamingContext context)
    {
      Context = context;
    }

    public SerializationBinder Binder { get; set; } = defaultBinder;
    public StreamingContext Context { get; set; }

    public object Deserialize(Stream stream)
    {
      return Deserialize(stream, Context);
    }

    public void Serialize(Stream stream, object obj)
    {
      Serialize(stream, Context, obj);
    }

    public ISurrogateSelector SurrogateSelector { get; set; } = new SurrogateSelector();

    /// <summary>
    ///   Same as regular Deserialze, except with a custom StreamingContext
    /// </summary>
    /// <param name="stream">Stream from which to deserialize one item</param>
    /// <param name="context">StreamingContext to use, instead of the instance Context</param>
    /// <returns></returns>
    public object Deserialize(Stream stream, StreamingContext context)
    {
      if (stream == null) {
        throw new ArgumentNullException(nameof(stream));
      }
      if (!stream.CanRead) {
        throw new ArgumentException("Stream is not readable", nameof(stream));
      }
      try {
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true)) {
          using (var cache = new Cache(context)) {
            return ReadObject(cache, reader);
          }
        }
      }
      catch (ArgumentOutOfRangeException ex) {
        throw new SerializationException("Failed to deserialize misformed stream", ex);
      }
    }

    /// <summary>
    ///   Same as regular Serialize, except with a custom StreamingContext
    /// </summary>
    /// <param name="stream">Stream to which to serialize one item</param>
    /// <param name="context">StreamingContext to use, instead of the instance Context</param>
    /// <param name="obj">Object to serialize</param>
    public void Serialize(Stream stream, StreamingContext context, object obj)
    {
      if (stream == null) {
        throw new ArgumentNullException(nameof(stream));
      }
      if (obj == null) {
        throw new ArgumentNullException(nameof(obj), "Cannot serialize freestanding null");
      }
      if (!stream.CanWrite) {
        throw new ArgumentException("Stream is not writable", nameof(stream));
      }
      try {
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
          using (var cache = new Cache(context)) {
            WriteObject(cache, writer, obj);
          }
        }
      }
      catch (ArgumentOutOfRangeException ex) {
        throw new SerializationException("Failed to serialize misformed object", ex);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MemberInfo[] GetSerializableMembersViaReflection(Cache cache, Type type)
    {
      if (type == null) {
        throw new ArgumentNullException(nameof(type));
      }
      return serializable.GetOrAdd(type, t => FormatterServices.GetSerializableMembers(t, cache.Context));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object ReadObject(Cache cache, BinaryReader reader)
    {
      var et = reader.ReadElementType();
      switch (et) {
      case ElementType.Null:
        return null;
      case ElementType.Array: {
        var at = reader.ReadType(cache, Binder);
        var dimensions = reader.ReadLength();
        var lower = new int[dimensions];
        var lens = new int[dimensions];

        for (var i = 0; i < dimensions; i++) {
          lower[i] = reader.ReadInt32();
          lens[i] = reader.ReadInt32();
        }
        var array = Array.CreateInstance(at, lens, lower);
        VisitElements(array, (arr, idx) => array.SetValue(ReadObject(cache, reader), idx));
        return array;
      }
      case ElementType.Object:
        return ReadSingleObject(cache, reader);
      case ElementType.ObjectRef: {
        var oid = reader.ReadLength();
        return cache.GetObjectForRef(oid);
      }
      case ElementType.Boolean:
        return reader.ReadBoolean();
      case ElementType.Byte:
        return reader.ReadByte();
      case ElementType.SByte:
        return reader.ReadSByte();
      case ElementType.Char:
        return reader.ReadChar();
      case ElementType.Single:
        return reader.ReadSingle();
      case ElementType.Double:
        return reader.ReadDouble();
      case ElementType.Int16:
        return reader.ReadInt16();
      case ElementType.Int32:
        return reader.ReadInt32();
      case ElementType.Int64:
        return reader.ReadInt64();
      case ElementType.Uint16:
        return reader.ReadUInt16();
      case ElementType.Uint32:
        return reader.ReadUInt32();
      case ElementType.Uint64:
        return reader.ReadUInt64();
      case ElementType.DateTime:
        return DateTime.FromBinary(reader.ReadInt64());
      case ElementType.TimeSpam:
        return new TimeSpan(reader.ReadInt64());
      case ElementType.Decimal:
        return reader.ReadDecimal();
      case ElementType.EmptyString:
        return string.Empty;
      case ElementType.String:
        return reader.ReadInternedString(cache);
      case ElementType.EmptyArray: {
        var eat = reader.ReadType(cache, Binder);
        return Array.CreateInstance(eat, 0);
      }
      case ElementType.ByteArray: {
        var blen = reader.ReadLength();
        return reader.ReadBytes(blen);
      }
      case ElementType.UnaryArray: {
        var uat = reader.ReadType(cache, Binder);
        var ulen = reader.ReadLength();
        var objects = (object[])Array.CreateInstance(uat, ulen);
        for (var i = 0; i < ulen; ++i) {
          objects[i] = ReadObject(cache, reader);
        }
        return objects;
      }
      case ElementType.EmptyPODArray: {
        var eat = reader.ReadElementType();
        var podtype = eat.ToPODType();
        if (podtype == null) {
          throw new ArgumentOutOfRangeException();
        }
        return Array.CreateInstance(podtype, 0);
      }
      case ElementType.UnaryPODArray: {
        var uat = reader.ReadElementType();
        var ulen = reader.ReadLength();
        var podtype = uat.ToPODType();
        if (podtype == null) {
          throw new ArgumentOutOfRangeException();
        }
        var podobjects = (object[])Array.CreateInstance(podtype, ulen);
        for (var i = 0; i < ulen; ++i) {
          podobjects[i] = ReadObject(cache, reader);
        }
        return podobjects;
      }
      case ElementType.SingleArray: {
        var uat = reader.ReadType(cache, Binder);
        var objects = (object[])Array.CreateInstance(uat, 1);
        objects[0] = ReadObject(cache, reader);
        return objects;
      }
      case ElementType.SinglePODArray: {
        var uat = reader.ReadElementType();
        var podtype = uat.ToPODType();
        if (podtype == null) {
          throw new ArgumentOutOfRangeException();
        }
        var podobjects = (object[])Array.CreateInstance(podtype, 1);
        podobjects[0] = ReadObject(cache, reader);
        return podobjects;
      }
      case ElementType.Enum: {
        var ut = reader.ReadType(cache, Binder);
        switch (ut.ToElementType()) {
        case ElementType.Byte:
          return Enum.ToObject(ut, reader.ReadByte());
        case ElementType.SByte:
          return Enum.ToObject(ut, reader.ReadSByte());
        case ElementType.Char:
          return Enum.ToObject(ut, reader.ReadChar());
        case ElementType.Int16:
          return Enum.ToObject(ut, reader.ReadInt16());
        case ElementType.Int32:
          return Enum.ToObject(ut, reader.ReadInt32());
        case ElementType.Int64:
          return Enum.ToObject(ut, reader.ReadInt64());
        case ElementType.Uint16:
          return Enum.ToObject(ut, reader.ReadUInt16());
        case ElementType.Uint32:
          return Enum.ToObject(ut, reader.ReadUInt32());
        case ElementType.Uint64:
          return Enum.ToObject(ut, reader.ReadUInt64());
        default:
          throw new ArgumentOutOfRangeException();
        }
      }
      default:
        throw new ArgumentOutOfRangeException();
      }
    }

    private object ReadSingleObject(Cache cache, BinaryReader reader)
    {
      object rv;
      var it = reader.ReadType(cache, Binder);
      if (it.IsEnum || it.IsPrimitive || it.IsArray) {
        throw new NotSupportedException();
      }
      var oid = reader.ReadLength();
      int flen;
      if (typeof (ISerializable).IsAssignableFrom(it)) {
        var ctor = GetDeserializationConstructor(it);
        var info = new SerializationInfo(it, converter);

        flen = reader.ReadLength();

        rv = FormatterServices.GetUninitializedObject(it);
        cache.RegisterObjectRef(oid, rv);
        for (var i = 0; i < flen; i++) {
          var name = reader.ReadInternedString(cache);
          var val = ReadObject(cache, reader);
          var ftype = val?.GetType() ?? typeof (object);
          info.AddValue(name, val, ftype);
        }
        ctor.Invoke(rv, new object[] {info, cache.Context});
        return rv;
      }
      rv = FormatterServices.GetUninitializedObject(it);
      cache.RegisterObjectRef(oid, rv);
      flen = reader.ReadLength();

      var fields = new Dictionary<string, object>(flen);
      for (var i = 0; i < flen; i++) {
        var name = reader.ReadInternedString(cache);
        var val = ReadObject(cache, reader);
        fields[name] = val;
      }

      var members = GetSerializableMembersViaReflection(cache, it);
      var values = new object[members.Length];

      for (var i = 0; i < members.Length; i++) {
        var member = members[i];
        object val;
        if (fields.TryGetValue(member.Name, out val)) {
          values[i] = val;
        }
        else if (!IsOptional(member)) {
          throw new SerializationException($"Stream does not contain a value for '{member.Name}'");
        }
      }
      FormatterServices.PopulateObjectMembers(rv, members, values);
      return rv;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteArray(Cache cache, BinaryWriter writer, object obj, Type type)
    {
      var at = type.GetElementType();
      var eat = at.ToElementType();
      var arr = (Array)obj;
      if (arr.Rank == 1) {
        var podtype = eat.ToPODType();
        if (arr.Length == 0) {
          if (podtype != null) {
            writer.Write(ElementType.EmptyPODArray);
            writer.Write(eat);
          }
          else {
            writer.Write(ElementType.EmptyArray);
            writer.Write(cache, Binder, at);
          }
          return;
        }
        if (arr.Length == 1) {
          if (podtype != null) {
            writer.Write(ElementType.SinglePODArray);
            writer.Write(eat);
            WriteObject(cache, writer, arr.GetValue(0));
          }
          else {
            writer.Write(ElementType.SingleArray);
            writer.Write(cache, Binder, at);
            WriteObject(cache, writer, arr.GetValue(0));
          }
          return;
        }

        if (at == typeof (byte)) {
          writer.Write(ElementType.ByteArray);
          writer.WriteLength(arr.Length);
          writer.Write((byte[])obj);
          return;
        }
        var objects = (object[])obj;
        if (podtype != null) {
          writer.Write(ElementType.UnaryPODArray);
          writer.Write(eat);
          writer.WriteLength(objects.Length);
          foreach (var o in objects) {
            WriteObject(cache, writer, o);
          }
          return;
        }
        writer.Write(ElementType.UnaryArray);
        writer.Write(cache, Binder, at);
        writer.WriteLength(objects.Length);
        foreach (var o in objects) {
          WriteObject(cache, writer, o);
        }
        return;
      }

      writer.Write(ElementType.Array);
      writer.Write(cache, Binder, at);
      writer.WriteLength(arr.Rank);
      for (var d = 0; d < arr.Rank; d++) {
        writer.Write(arr.GetLowerBound(d));
        writer.Write(arr.GetLength(d));
      }
      VisitElements(arr, (a, idx) => WriteObject(cache, writer, a.GetValue(idx)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteObject(Cache cache, BinaryWriter writer, object obj)
    {
      if (obj == null) {
        writer.Write(ElementType.Null);
        return;
      }
      var type = obj.GetType();
      int oid;
      if (!type.IsValueType && cache.GetObjectRef(obj, out oid)) {
        writer.Write(ElementType.ObjectRef);
        writer.WriteLength(oid);
        return;
      }
      var et = type.ToElementType();

      switch (et) {
      case ElementType.Array:
        WriteArray(cache, writer, obj, type);
        return;
      case ElementType.Object: {
        writer.Write(ElementType.Object);
        writer.Write(cache, Binder, type);
        oid = cache.RegisterObjectRef(obj);
        writer.WriteLength(oid);
        var ser = obj as ISerializable;
        if (ser != null) {
          var info = new SerializationInfo(type, converter);
          ser.GetObjectData(info, cache.Context);
          writer.WriteLength(info.MemberCount);
          var enumerator = info.GetEnumerator();
          while (enumerator.MoveNext()) {
            var entry = enumerator.Current;
            writer.WriteInterned(cache, entry.Name);
            WriteObject(cache, writer, entry.Value);
          }
          return;
        }
        var members = GetSerializableMembersViaReflection(cache, type);
        writer.WriteLength(members.Length);
        var values = FormatterServices.GetObjectData(obj, members);
        for (var i = 0; i < members.Length; i++) {
          var name = members[i].Name;
          writer.WriteInterned(cache, name);
          WriteObject(cache, writer, values[i]);
        }
        return;
      }
      case ElementType.Boolean:
        writer.Write(et);
        writer.Write((bool)obj);
        return;
      case ElementType.Byte:
        writer.Write(et);
        writer.Write((byte)obj);
        return;
      case ElementType.SByte:
        writer.Write(et);
        writer.Write((sbyte)obj);
        return;
      case ElementType.Char:
        writer.Write(et);
        writer.Write((char)obj);
        return;
      case ElementType.Single:
        writer.Write(et);
        writer.Write((float)obj);
        return;
      case ElementType.Double:
        writer.Write(et);
        writer.Write((double)obj);
        return;
      case ElementType.Int16:
        writer.Write(et);
        writer.Write((short)obj);
        return;
      case ElementType.Int32:
        writer.Write(et);
        writer.Write((int)obj);
        return;
      case ElementType.Int64:
        writer.Write(et);
        writer.Write((long)obj);
        return;
      case ElementType.Uint16:
        writer.Write(et);
        writer.Write((ushort)obj);
        return;
      case ElementType.Uint32:
        writer.Write(et);
        writer.Write((uint)obj);
        return;
      case ElementType.Uint64:
        writer.Write(et);
        writer.Write((ulong)obj);
        return;
      case ElementType.DateTime:
        writer.Write(et);
        writer.Write(((DateTime)obj).ToBinary());
        return;
      case ElementType.TimeSpam:
        writer.Write(et);
        writer.Write(((TimeSpan)obj).Ticks);
        return;
      case ElementType.Decimal:
        writer.Write(et);
        writer.Write((decimal)obj);
        return;
      case ElementType.String:
        var s = (string)obj;
        if (string.IsNullOrEmpty(s)) {
          writer.Write(ElementType.EmptyString);
          return;
        }
        writer.Write(et);
        writer.WriteInterned(cache, s);
        return;
      case ElementType.Enum: {
        var ut = type.GetEnumUnderlyingType();
        switch (ut.ToElementType()) {
        case ElementType.Byte:
          writer.Write((byte)obj);
          return;
        case ElementType.SByte:
          writer.Write((sbyte)obj);
          return;
        case ElementType.Int16:
          writer.Write((short)obj);
          return;
        case ElementType.Int32:
          writer.Write((int)obj);
          return;
        case ElementType.Int64:
          writer.Write((long)obj);
          return;
        case ElementType.Uint16:
          writer.Write((ushort)obj);
          return;
        case ElementType.Uint32:
          writer.Write((uint)obj);
          return;
        case ElementType.Uint64:
          writer.Write((ulong)obj);
          return;
        default:
          throw new ArgumentOutOfRangeException();
        }
      }
      default:
        throw new ArgumentOutOfRangeException();
      }
    }
  }
}
