using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace NMaier.Serialization
{
  public sealed class EfficientSerializationBinder : SerializationBinder
  {
    private const string MONO = "Mono.";
    private const string MS = "Microsoft.";
    private const string SYSTEM = "System.";

    private static readonly ConcurrentDictionary<string, Type> binders =
      new ConcurrentDictionary<string, Type>();

    private static int cknown;
    private static readonly ConcurrentDictionary<string, string> known = new ConcurrentDictionary<string, string>();
    private static readonly ConcurrentDictionary<string, string> revknown = new ConcurrentDictionary<string, string>();

    /// <summary>
    ///   Register another known assembly.
    ///   Known assemblies will allow to use a shorter presentation.
    ///   Plase note: You have to register additional assemblies in a deterministic order
    /// </summary>
    /// <param name="assembly">Assembly to register</param>
    public static void RegisterKnown(Assembly assembly)
    {
      var aid = Interlocked.Increment(ref cknown);
      var name = assembly.GetName().Name;
      var said = $"`{aid.ToString("x")}";
      if (known.TryAdd(name, said)) {
        revknown.AddOrUpdate(said, name, (k, v) => v);
      }
    }

    /// <summary>
    ///   Register another set assemblies.
    ///   Known assemblies will allow to use a shorter presentation.
    ///   Plase note: You have to register additional assemblies in a deterministic order
    /// </summary>
    /// <param name="assemblies">Assemblies to register</param>
    public static void RegisterKnown(params Assembly[] assemblies)
    {
      foreach (var i in assemblies) {
        RegisterKnown(i);
      }
    }

    /// <summary>
    ///   Register another assembly.
    ///   Known assemblies will allow to use a shorter presentation.
    ///   Plase note: You have to register additional assemblies in a deterministic order
    /// </summary>
    /// <param name="type">Type located in assembly to register</param>
    public static void RegisterKnown(Type type)
    {
      RegisterKnown(type.Assembly);
    }

    /// <summary>
    ///   Register another set of assemblies.
    ///   Known assemblies will allow to use a shorter presentation.
    ///   Plase note: You have to register additional assemblies in a deterministic order
    /// </summary>
    /// <param name="types">Types located in assemblies to register</param>
    public static void RegisterKnown(params Type[] types)
    {
      foreach (var i in types) {
        RegisterKnown(i.Assembly);
      }
    }

    static EfficientSerializationBinder()
    {
      RegisterKnown(typeof (string));
      RegisterKnown(typeof (Array));
      RegisterKnown(typeof (ArrayList));
      RegisterKnown(typeof (List<>));
      RegisterKnown(typeof (ConcurrentDictionary<,>));
      RegisterKnown(typeof (Thread));
      RegisterKnown(typeof (ASCIIEncoding));
      RegisterKnown(typeof (Regex));
      RegisterKnown(typeof (Enumerable));
      RegisterKnown(typeof (AcceptRejectRule));
      RegisterKnown(typeof (ConformanceLevel));
      RegisterKnown(typeof (LoadOptions).Assembly);
    }

    private static Assembly Load(AssemblyName name)
    {
#pragma warning disable 618
      return Assembly.LoadWithPartialName(name.Name);
#pragma warning restore 618
    }

    private static Type Lookup(string type)
    {
      return Type.GetType(type, Load, null, true);
    }

    public override void BindToName(Type serializedType, out string assemblyName,
      out string typeName)
    {
      if (serializedType == null) {
        throw new ArgumentNullException(nameof(serializedType));
      }
      typeName = serializedType.FullName;
      assemblyName = serializedType.Assembly.GetName().Name;
      if (typeName.StartsWith(assemblyName)) {
        typeName = $"*{typeName.Substring(assemblyName.Length + 1)}";
      }
      else if (typeName.StartsWith(SYSTEM)) {
        typeName = $"?{typeName.Substring(SYSTEM.Length)}";
      }
      else if (typeName.StartsWith(MS)) {
        typeName = $"+{typeName.Substring(MS.Length)}";
      }
      else if (typeName.StartsWith(MONO)) {
        typeName = $"<{typeName.Substring(MS.Length)}";
      }
      string rassembly;
      if (known.TryGetValue(assemblyName, out rassembly)) {
        assemblyName = rassembly;
      }
    }

    public override Type BindToType(string assemblyName, string typeName)
    {
      if (assemblyName == null) {
        throw new ArgumentNullException(nameof(assemblyName));
      }
      if (typeName == null) {
        throw new ArgumentNullException();
      }

      string rassembly;
      if (!revknown.TryGetValue(assemblyName, out rassembly)) {
        rassembly = assemblyName;
      }
      if (typeName.StartsWith("*")) {
        typeName = $"{assemblyName}.{typeName.Substring(1)}";
      }
      else if (typeName.StartsWith("*")) {
        typeName = $"{SYSTEM}{typeName.Substring(1)}";
      }
      else if (typeName.StartsWith("+")) {
        typeName = $"{MS}{typeName.Substring(1)}";
      }
      else if (typeName.StartsWith("<")) {
        typeName = $"{MONO}{typeName.Substring(1)}";
      }
      var serializationType = $"{typeName}, {rassembly}";
      return binders.GetOrAdd(serializationType, Lookup);
    }
  }
}
