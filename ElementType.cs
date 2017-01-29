namespace NMaier.Serialization
{
  internal enum ElementType : byte
  {
    Null = 0x0,
    Array = 0x1,
    Object = 0x2,
    ObjectRef = 0x3,
    Boolean = 0x10,
    Byte = 0x11,
    SByte = 0x12,
    Char = 0x13,
    Single = 0x14,
    Double = 0x15,
    Int16 = 0x16,
    Int32 = 0x17,
    Int64 = 0x18,
    Uint16 = 0x19,
    Uint32 = 0x1A,
    Uint64 = 0x1B,
    DateTime = 0x20,
    TimeSpam = 0x21,
    Decimal = 0x30,
    String = 0x40,
    EmptyString = 0x41,
    ByteArray = 0x50,
    EmptyArray = 0x51,
    UnaryArray = 0x52,
    EmptyPODArray = 0x53,
    UnaryPODArray = 0x54,
    SingleArray = 0x55,
    SinglePODArray = 0x56,
    Enum = 0x60,
    NullableT = 0x61,
    Nullable = 0x60
  }
}
