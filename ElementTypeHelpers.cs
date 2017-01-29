using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;

namespace NMaier.Serialization
{
  internal static class ElementTypeHelpers
  {
    private static readonly ConcurrentDictionary<Type, bool> typeserializable = new ConcurrentDictionary<Type, bool>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ElementType ReadElementType(this BinaryReader reader)
    {
      return (ElementType)reader.ReadByte();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string ReadInternedString(this BinaryReader reader, IAtomize atomize)
    {
      var sid = reader.ReadLength();
      var rv = atomize.GetString(sid, reader.ReadStringInternal);
      return rv;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ReadLength(this BinaryReader reader)
    {
      var b = reader.ReadByte();
      if (b == 0xfe) {
        return reader.ReadUInt16();
      }
      if (b == 0xff) {
        return reader.ReadInt32();
      }
      return b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Type ReadType(this BinaryReader reader, IAtomize atomize,
      SerializationBinder binder)
    {
      var assembly = reader.ReadInternedString(atomize);
      var typename = reader.ReadInternedString(atomize);
      return binder.BindToType(assembly, typename);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ElementType ToElementType(this Type type)
    {
      if (type.IsEnum) {
        return ElementType.Enum;
      }
      if (type.IsArray) {
        return ElementType.Array;
      }
      if (type == typeof (bool)) {
        return ElementType.Boolean;
      }
      if (type == typeof (int)) {
        return ElementType.Int32;
      }
      if (type == typeof (uint)) {
        return ElementType.Uint32;
      }
      if (type == typeof (byte)) {
        return ElementType.Byte;
      }
      if (type == typeof (sbyte)) {
        return ElementType.SByte;
      }
      if (type == typeof (string)) {
        return ElementType.String;
      }
      if (type == typeof (float)) {
        return ElementType.Single;
      }
      if (type == typeof (double)) {
        return ElementType.Double;
      }
      if (type == typeof (char)) {
        return ElementType.Char;
      }
      if (type == typeof (long)) {
        return ElementType.Int64;
      }
      if (type == typeof (ulong)) {
        return ElementType.Uint64;
      }
      if (type == typeof (short)) {
        return ElementType.Int16;
      }
      if (type == typeof (ushort)) {
        return ElementType.Uint16;
      }
      if (type == typeof (DateTime)) {
        return ElementType.DateTime;
      }
      if (type == typeof (TimeSpan)) {
        return ElementType.TimeSpam;
      }
      if (type == typeof (decimal)) {
        return ElementType.Decimal;
      }
      if (type == typeof (Nullable<>)) {
        return ElementType.NullableT;
      }
      if (type == typeof (Nullable)) {
        return ElementType.Nullable;
      }
      if (!type.IsPrimitive) {
        return ElementType.Object;
      }
      throw new NotSupportedException();
    }

    internal static Type ToPODType(this ElementType et)
    {
      switch (et) {
      case ElementType.Boolean:
        return typeof (bool);
      case ElementType.Byte:
        return typeof (byte);
      case ElementType.SByte:
      case ElementType.Char:
        return typeof (char);
      case ElementType.Single:
        return typeof (float);
      case ElementType.Double:
        return typeof (double);
      case ElementType.Int16:
        return typeof (short);
      case ElementType.Int32:
        return typeof (int);
      case ElementType.Int64:
        return typeof (long);
      case ElementType.Uint16:
        return typeof (ushort);
      case ElementType.Uint32:
        return typeof (uint);
      case ElementType.Uint64:
        return typeof (ulong);
      case ElementType.DateTime:
        return typeof (DateTime);
      case ElementType.TimeSpam:
        return typeof (TimeSpan);
      case ElementType.Decimal:
        return typeof (decimal);
      case ElementType.String:
        return typeof (string);
      default:
        return null;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Write(this BinaryWriter writer, ElementType et)
    {
      writer.Write((byte)et);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Write(this BinaryWriter writer, IAtomize atomize,
      SerializationBinder binder, Type type)
    {
      if (type == null) {
        throw new ArgumentNullException(nameof(type));
      }
      EnsureTypeIsSerializable(type);
      string name;
      string assembly;
      binder.BindToName(type, out assembly, out name);
      if (assembly == null) {
        assembly = type.Assembly.FullName;
      }
      if (name == null) {
        name = type.FullName;
      }
      writer.WriteInterned(atomize, assembly);
      writer.WriteInterned(atomize, name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteInterned(this BinaryWriter writer, IAtomize atomize, string str)
    {
      int sid;
      if (atomize.AtomizeString(str, out sid)) {
        writer.WriteLength(sid);
        return;
      }
      writer.WriteLength(sid);
      var bytes = Encoding.UTF8.GetBytes(str);
      writer.WriteLength(bytes.Length);
      writer.Write(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteLength(this BinaryWriter writer, int length)
    {
      if (length < 0) {
        throw new ArgumentOutOfRangeException();
      }
      if (length > 0xfe) {
        if (length > ushort.MaxValue) {
          writer.Write((byte)0xff);
          writer.Write(length);
        }
        else {
          writer.Write((byte)0xfe);
          writer.Write((ushort)length);
        }
      }
      else {
        writer.Write((byte)length);
      }
    }

    private static void EnsureTypeIsSerializable(Type type)
    {
      if (!typeserializable.GetOrAdd(type, EnsureTypeIsSerializableInternal)) {
        throw new SerializationException($"Type {type.FullName} is not Serializable");
      }
    }

    private static bool EnsureTypeIsSerializableInternal(Type type)
    {
      return type.Attributes.HasFlag(TypeAttributes.Serializable);
    }

    private static string ReadStringInternal(this BinaryReader reader)
    {
      var len = reader.ReadLength();
      var bytes = reader.ReadBytes(len);
      return Encoding.UTF8.GetString(bytes);
    }
  }
}
