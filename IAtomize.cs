using System;

namespace NMaier.Serialization
{
  internal interface IAtomize
  {
    bool AtomizeString(string s, out int id);
    string GetString(int id, Func<string> func);
  }
}
