#region License
/*
  ShaderDecompiler - Direct3D shader decompiler

  Released under Microsoft Public License
  See LICENSE for details
*/
#endregion

namespace ShaderDecompiler.Structures;

public class Technique : AnnotatedObject
{
    public string? Name;
    public Pass[] Passes = [];

    public override string ToString()
    {
        return $"technique {Name}\n{{\n{string.Join("\n", Passes.Select(p => "\t" + p.ToString()!.Replace("\n", "\n\t")))}\n}}";
    }
}

public class Pass : AnnotatedObject
{
    public string? Name;
    public State[] States = [];

    public override string ToString()
    {
        return $"pass {Name}\n{{\n{string.Join("\n", States.Select(p => "\t" + p.ToString()!.Replace("\n", "\n\t")))}\n}}";
    }
}

public class State
{
    public bool Ignored = false;
    public string? Name;
    public StateType Type;
    public Value Value = null!;

    public override string ToString()
    {
        return $"{Type};";
    }
}

public enum StateType
{
    VertexShader = 146,
    PixelShader = 147,
}
