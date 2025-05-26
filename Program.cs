#region License
/*
  ShaderDecompiler - Direct3D shader decompiler

  Released under Microsoft Public License
  See LICENSE for details
*/
#endregion

using ShaderDecompiler.XNACompatibility;

namespace ShaderDecompiler;

public static class Program {
	public static void Main() {
		var fxcAndXnbFiles = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.*", SearchOption.TopDirectoryOnly)
		                              .Where(file => file.EndsWith(".fxc", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
		                              .ToList();

		foreach (var file in fxcAndXnbFiles) {
			Console.WriteLine("Reading file: " + file);

			using var reader = new BinaryReader(File.OpenRead(file));
			var isXnb = XnbReader.CheckHeader(reader);
			{
				reader.BaseStream.Seek(0, SeekOrigin.Begin);
			}

			Console.WriteLine("    Is XNB: " + isXnb);

			var effect = isXnb ? XnbReader.ReadEffect(reader) : Effect.Read(reader);

			Console.WriteLine("    Parameters: " + effect.Parameters.Length);
			{
				foreach (var parameter in effect.Parameters) {
					Console.WriteLine("        " + parameter);
				}
			}

			Console.WriteLine("    Techniques: " + effect.Techniques.Length);
			{
				foreach (var technique in effect.Techniques) {
					Console.WriteLine("        " + technique.Name);
					foreach (var pass in technique.Passes) {
						Console.WriteLine("            " + pass.Name);
					}
				}
			}
		}
	}
}