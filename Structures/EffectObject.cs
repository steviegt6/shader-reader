#region License
/*
  ShaderDecompiler - Direct3D shader decompiler

  Released under Microsoft Public License
  See LICENSE for details
*/
#endregion

namespace ShaderDecompiler.Structures;

public class EffectObject
{
    public object? Object;
    public ObjectType Type;

    public override string ToString()
    {
        return Type.ToString();
    }
}
