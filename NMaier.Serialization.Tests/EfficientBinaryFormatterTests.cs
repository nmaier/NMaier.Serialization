using NUnit.Framework;
using NMaier.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace NMaier.Serialization.Tests
{
  [TestFixture()]
  public class EfficientBinaryFormatterTests
  {
    [Test()]
    public void EfficientBinaryFormatterTest()
    {
      Assert.DoesNotThrow(() => new EfficientBinaryFormatter());
      Assert.DoesNotThrow(() => new EfficientBinaryFormatter(new StreamingContext()));
    }

    [Test()]
    public void DeserializeTest()
    {
      var formatter = new EfficientBinaryFormatter();
      var s = new Something() {d = 3.0, i = 1, s = "foo"};
      var o = new OtherThing(100);
      using (var ms = new MemoryStream()) {
        formatter.Serialize(ms, "test");
        formatter.Serialize(ms, 1);
        formatter.Serialize(ms, s);
        formatter.Serialize(ms, o);
        ms.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(formatter.Deserialize(ms), "test");
        Assert.AreEqual(formatter.Deserialize(ms), 1);
        Assert.AreEqual(s, formatter.Deserialize(ms));
        var other = (OtherThing)formatter.Deserialize(ms);
        Assert.AreEqual(other.self, other);
        Assert.AreEqual(other.b, true);
        Assert.AreEqual(other.s, o.s);
        Assert.AreEqual(other.arr1, o.arr1);
        Assert.AreEqual(other.arr2, o.arr2);
        Assert.AreEqual(other.arr3, o.arr3);
        Assert.AreEqual(other.arr4[0], other.self);
        Assert.AreNotEqual(other.i, o.i);
      }
    }

    [Test()]
    public void SerializeTest()
    {
      var formatter = new EfficientBinaryFormatter();
      var o = new object();
      var s = new Something() {d = 3.0, i = 1, s = "foo"};
      using (var ms = new MemoryStream()) {
        Assert.Throws<ArgumentNullException>(() => formatter.Serialize(ms, null));
        Assert.DoesNotThrow(() => formatter.Serialize(ms, "abc"));
        Assert.DoesNotThrow(() => formatter.Serialize(ms, 1));
        Assert.DoesNotThrow(() => formatter.Serialize(ms, 1.0));
        Assert.DoesNotThrow(() => formatter.Serialize(ms, o));
        Assert.DoesNotThrow(() => formatter.Serialize(ms, 1.2d));
        Assert.DoesNotThrow(() => formatter.Serialize(ms, s));
        using (var ms2 = new MemoryStream()) {
          Assert.DoesNotThrow(() => formatter.Serialize(ms2, "abc"));
          Assert.DoesNotThrow(() => formatter.Serialize(ms2, 1));
          Assert.DoesNotThrow(() => formatter.Serialize(ms2, 1.0));
          Assert.DoesNotThrow(() => formatter.Serialize(ms2, o));
          Assert.DoesNotThrow(() => formatter.Serialize(ms2, 1.2d));
          Assert.DoesNotThrow(() => formatter.Serialize(ms2, s));

          Assert.AreEqual(ms.ToArray(), ms2.ToArray());
        }
      }
    }

    [Serializable]
    private struct Something
    {
      internal string s;
      internal int i;
      internal double d;
    }

    [Serializable]
    private class OtherThing
    {
      internal readonly OtherThing self;
      internal string s = "abc";
      internal readonly bool b = true;

      [NonSerialized]
      internal int i;

      internal string[] arr1 = new[] { "a" };
      internal string[] arr2 = new string[0];
      internal byte[] arr3 = new byte[] { 255, 0, 1 };
      internal OtherThing[] arr4;
      internal Custom custom;
      internal long? nullable = null;

      public OtherThing(int i)
      {
        this.i = i;
        self = this;
        arr4 = new[] {this};
        custom = new Custom(i);
      }

      [Serializable]
      internal class Custom : ISerializable
      {
        private Custom self;
        private int i;
        public Custom(int i)
        {
          this.i = i;
          self = this;
        }
        protected Custom(SerializationInfo info, StreamingContext context)
        {
          self = (Custom)info.GetValue("self", typeof(Custom));
          i = info.GetInt32("i");
        }
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
          info.AddValue("i", i);
          info.AddValue("self", self);
        }
      }
    }
  }
}