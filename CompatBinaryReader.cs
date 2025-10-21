#if NETSTANDARD2_0
global using BinaryReader = ShaderDecompiler.CompatBinaryReader;
using System.Text;

namespace ShaderDecompiler;

public class CompatBinaryReader : System.IO.BinaryReader
{
    public CompatBinaryReader(Stream input) : base(input) { }
    public CompatBinaryReader(Stream input, Encoding encoding) : base(input, encoding) { }
    public CompatBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen) { }

    public new int Read7BitEncodedInt()
    {
        return base.Read7BitEncodedInt();
    }
}
#endif
