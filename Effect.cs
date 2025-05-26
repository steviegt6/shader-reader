#region License
/*
  ShaderDecompiler - Direct3D shader decompiler

  Released under Microsoft Public License
  See LICENSE for details
*/
#endregion

using System.Diagnostics;
using System.Text;

using ShaderDecompiler.Structures;
using ShaderDecompiler.XNACompatibility;

namespace ShaderDecompiler;

public class Effect {
	private long basePosition;
	private BinaryReader reader = null!; // not null when reading

	public Parameter[] Parameters = [];
	public Technique[] Techniques = [];
	public EffectObject[] Objects = [];

	public static Effect ReadXnbOrFxc(string file, out bool xnb)
	{
		return ReadXnbOrFxc(File.OpenRead(file), out xnb);
	}
	
	public static Effect ReadXnbOrFxc(Stream stream, out bool xnb)
	{
		return ReadXnbOrFxc(new BinaryReader(stream), out xnb);
	}
	
	public static Effect ReadXnbOrFxc(BinaryReader reader, out bool xnb)
	{
		xnb = XnbReader.CheckHeader(reader);
		{
			reader.BaseStream.Seek(0, SeekOrigin.Begin);
		}
		
		return xnb ? XnbReader.ReadEffect(reader) : Read(reader);
	}
	
	public static Effect Read(BinaryReader reader) {
		var magic = reader.ReadUInt32();
		if (magic == 0xbcf00bcf) {
			var skip = reader.ReadUInt32() - 8;
			reader.BaseStream.Seek(skip, SeekOrigin.Current);

			magic = reader.ReadUInt32();
		}

		if (magic != 0xfeff0901)
		{
			throw new NotEffectDataException();
		}

		Effect effect = new();
		effect.LoadEffect(reader);
		return effect;

		//uint type = (magic & 0xffff0000) >> 16;
		//reader.BaseStream.Seek(-4, SeekOrigin.Current);
		//
		//if (type == 0xffff) // only PixelShader
		//{
		//	throw new NotImplementedException();
		//}
		//else if (type == 0xfffe) // only VertexShader
		//{
		//	throw new NotImplementedException();
		//}
		//else throw new InvalidDataException();
	}

	private void LoadEffect(BinaryReader reader) {
		this.reader = reader;
		var offset = reader.ReadUInt32();

		basePosition = reader.BaseStream.Position;

		reader.BaseStream.Seek(offset, SeekOrigin.Current);

		var numparams = reader.ReadUInt32();
		var numtechniques = reader.ReadUInt32();
		reader.ReadUInt32();
		var numobjects = reader.ReadUInt32();

		if (numobjects > 0)
		{
			Objects = new EffectObject[numobjects];
		}

		ReadParameters(numparams);
		ReadTechniques(numtechniques);

		this.reader = null!;
	}

	private void ReadParameters(uint count) {
		if (count == 0)
		{
			return;
		}

		Parameters = new Parameter[count];
		for (var i = 0; i < count; i++) {
			Parameter p = new();
			Parameters[i] = p;

			var typeptr = reader.ReadUInt32();
			var valueptr = reader.ReadUInt32();
			p.Flags = reader.ReadUInt32();
			var numannotations = reader.ReadUInt32();

			ReadAnnotations(numannotations, p);
			p.Value = ReadValue(typeptr, valueptr);

		}
	}

	private void ReadTechniques(uint count) {
		if (count == 0)
		{
			return;
		}

		Techniques = new Technique[count];

		for (var t = 0; t < count; t++) {
			Technique tech = new();
			Techniques[t] = tech;

			tech.Name = ReadString();
			var numannotations = reader.ReadUInt32();
			var numpasses = reader.ReadUInt32();

			ReadAnnotations(numannotations, tech);

			if (numpasses == 0)
			{
				continue;
			}

			tech.Passes = new Pass[numpasses];

			for (var p = 0; p < numpasses; p++) {
				Pass pass = new();
				tech.Passes[p] = pass;

				pass.Name = ReadString();
				numannotations = reader.ReadUInt32();
				var numstates = reader.ReadUInt32();

				ReadAnnotations(numannotations, pass);

				if (numstates == 0)
				{
					continue;
				}

				pass.States = new State[numstates];
				for (var s = 0; s < numstates; s++) {
					State state = new();
					pass.States[s] = state;

					state.Type = (StateType)reader.ReadUInt32();

					reader.ReadUInt32();
					var typeptr = reader.ReadUInt32();
					var valueptr = reader.ReadUInt32();

					state.Value = ReadValue(typeptr, valueptr);
				}
			}
		}
	}

	private void ReadAnnotations(uint count, AnnotatedObject @object) {
		if (count == 0)
		{
			return;
		}

		@object.Annotations = new Value[count];
		for (var i = 0; i < count; i++) {
			var typeptr = reader.ReadUInt32();
			var valueptr = reader.ReadUInt32();

			@object.Annotations[i] = ReadValue(typeptr, valueptr);
		}
	}

	private Value ReadValue(uint typeptr, uint valueptr) {
		var readerpos = reader.BaseStream.Position;
		try {
			Value value = new();

			reader.BaseStream.Seek(basePosition + typeptr, SeekOrigin.Begin);
			ReadValueInfo(value);

			reader.BaseStream.Seek(basePosition + valueptr, SeekOrigin.Begin);
			ReadValueData(value);

			return value;
		}
		finally {
			reader.BaseStream.Seek(readerpos, SeekOrigin.Begin);
		}
	}

	private void ReadValueInfo(Value value) {
		value.Type.Type = (ObjectType)reader.ReadUInt32();
		value.Type.Class = (ObjectClass)reader.ReadUInt32();
		value.Name = ReadString();
		value.Semantic = ReadString();
		value.Type.Elements = reader.ReadUInt32();

		if (value.Type.Class is >= ObjectClass.Scalar and <= ObjectClass.MatrixColumns) {
			value.Type.Columns = reader.ReadUInt32();
			value.Type.Rows = reader.ReadUInt32();
		}
		else if (value.Type.Class == ObjectClass.Struct) {
			var members = reader.ReadUInt32();
			List<Value> memberList = [];
			for (var i = 0; i < members; i++) {
				Value m = new();
				ReadValueInfo(m);
				if (m.Type.Class == ObjectClass.Struct)
				{
					members--;
				}

				memberList.Add(m);
			}
			value.Type.StructMembers = memberList.ToArray();
		}
	}

	private void ReadValueData(Value value) {
		if (value.Type.Class is >= ObjectClass.Scalar and <= ObjectClass.MatrixColumns) {
			var size = value.Type.Columns * value.Type.Rows;
			if (value.Type.Elements > 0)
			{
				size *= value.Type.Elements;
			}

			switch (value.Type.Type) {
				case ObjectType.Int:
					var ints = new int[size];
					for (var i = 0; i < size; i++)
					{
						ints[i] = reader.ReadInt32();
					}
					value.Object = ints;
					break;

				case ObjectType.Float:
					var floats = new float[size];
					for (var i = 0; i < size; i++)
					{
						floats[i] = reader.ReadSingle();
					}
					value.Object = floats;
					break;

				case ObjectType.Bool:
					var bools = new bool[size];
					for (var i = 0; i < size; i++)
					{
						bools[i] = reader.ReadBoolean();
					}
					value.Object = bools;
					break;

				default:
					Debugger.Break();
					break;
			}

		}
		else if (value.Type.Class == ObjectClass.Object) {
			if (value.Type.Type is >= ObjectType.Sampler and <= ObjectType.Samplercube) {
				var numstates = reader.ReadUInt32();

				var states = new SamplerState[numstates];

				for (var i = 0; i < numstates; i++) {
					SamplerState state = new();
					states[i] = state;

					state.Type = (SamplerStateType)(reader.ReadUInt32() & ~0xA0);
					var something = reader.ReadUInt32();

					var statetypeptr = reader.ReadUInt32();
					var statevalueptr = reader.ReadUInt32();
					state.Value = ReadValue(statetypeptr, statevalueptr);

					if (state is { Type: SamplerStateType.Texture, Value.Object: uint[] idarray }) {
						Objects[idarray[0]] = new EffectObject {
							Type = value.Type.Type,
						};
					}
				}
				value.Object = states;
			}
			else {
				var count = Math.Max(value.Type.Elements, 1);
				var ids = new uint[count];

				for (var i = 0; i < count; i++) {
					ids[i] = reader.ReadUInt32();
					Objects[ids[i]] = new EffectObject {
						Type = value.Type.Type,
					};
				}

				value.Object = ids;
			}
		}
		else if (value.Type.Class == ObjectClass.Struct) {
			for (var i = 0; i < value.Type.StructMembers.Length; i++)
			{
				ReadValueData(value.Type.StructMembers[i]);
			}
		}
	}

	private string? ReadString() {
		var ptr = reader.ReadUInt32();
		if (ptr == 0 || basePosition + ptr >= reader.BaseStream.Length)
		{
			return null;
		}

		var readerpos = reader.BaseStream.Position;
		try {
			reader.BaseStream.Seek(basePosition + ptr, SeekOrigin.Begin);

			var len = reader.ReadUInt32();
			return ReadStringHere(len);
		}
		finally {
			reader.BaseStream.Seek(readerpos, SeekOrigin.Begin);
		}
	}

	private string? ReadStringHere(uint length) {
		if (length == 0)
		{
			return null;
		}

		return Encoding.ASCII.GetString(reader.ReadBytes((int)length - 1));
	}

	public class NotEffectDataException : Exception {

	}
}