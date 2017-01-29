using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;

namespace NMaier.Serialization
{
  internal sealed class Cache : IAtomize, IDisposable
  {
    private readonly ConcurrentDictionary<string, int> atoms =
      new ConcurrentDictionary<string, int>();

    private readonly IDictionary<object, int> idrefs = new Dictionary<object, int>();

    private readonly IDictionary<int, object> objrefs = new Dictionary<int, object>();

    private readonly ConcurrentDictionary<int, string> revatoms =
      new ConcurrentDictionary<int, string>();

    private int coid;

    private int csid;

    public Cache(StreamingContext context)
    {
      Context = context;
    }

    public StreamingContext Context { get; }

    public bool AtomizeString(string str, out int id)
    {
      var exists = true;
      id = atoms.GetOrAdd(str, s =>
      {
        exists = false;
        return Interlocked.Increment(ref csid);
      });
      return exists;
    }

    public string GetString(int id, Func<string> func)
    {
      return revatoms.GetOrAdd(id, i => func());
    }

    public void Dispose()
    {
      atoms.Clear();
      revatoms.Clear();
      idrefs.Clear();
      objrefs.Clear();
    }

    public object GetObjectForRef(int oid)
    {
      object rv;
      if (!objrefs.TryGetValue(oid, out rv)) {
        throw new ArgumentOutOfRangeException();
      }
      return rv;
    }

    public bool GetObjectRef(object obj, out int oid)
    {
      return idrefs.TryGetValue(obj, out oid);
    }

    public void RegisterObjectRef(int oid, object obj)
    {
      objrefs.Add(oid, obj);
    }

    public int RegisterObjectRef(object o)
    {
      var oid = ++coid;
      idrefs.Add(o, oid);
      return oid;
    }
  }
}
