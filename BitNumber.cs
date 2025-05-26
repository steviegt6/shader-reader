#region License
/*
  ShaderDecompiler - Direct3D shader decompiler

  Released under Microsoft Public License
  See LICENSE for details
*/
#endregion

namespace ShaderDecompiler;

public struct BitNumber {
	private readonly uint number;

	public bool this[int index] => ((number >> index) & 1) != 0;
	public uint this[Range range] {
		get {
			var start = range.Start.IsFromEnd ? 0 : range.Start.Value;
			var end = range.End.IsFromEnd ? 31 : range.End.Value;

			var len = end - start + 1;
			uint mask = 0;
			for (var i = 0; i < len; i++)
			{
				mask = (mask << 1) | 1;
			}
			mask <<= start;
			return (number & mask) >> start;
		}
	}

	public BitNumber(uint number) {
		this.number = number;
	}
}
