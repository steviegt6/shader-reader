#region License
/*  LzxDecoder.cs was derived from libmspack
	Copyright 2003-2004 Stuart Caie
	Copyright 2011 Ali Scissons

	The LZX method was created by Jonathan Forbes and Tomi Poutanen, adapted
	by Microsoft Corporation.

	This source file is Dual licensed; meaning the end-user of this source file
	may redistribute/modify it under the LGPL 2.1 or MS-PL licenses.

	About
	-----
	This derived work is recognized by Stuart Caie and is authorized to adapt
	any changes made to lzxd.c in his libmspack library and will still retain
	this dual licensing scheme. Big thanks to Stuart Caie!

	This file is a pure C# port of the lzxd.c file from libmspack, with minor
	changes towards the decompression of XNB files. The original decompression
	software of LZX encoded data was written by Suart Caie in his
	libmspack/cabextract projects, which can be located at
	http://http://www.cabextract.org.uk/

	GNU Lesser General Public License, Version 2.1
	----------------------------------------------
	https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html

	Microsoft Public License
	------------------------
	http://www.opensource.org/licenses/ms-pl.html
 */
#endregion

namespace ShaderDecompiler.XNACompatibility;

internal class LzxDecoder {
	public static uint[]? PositionBase;
	public static byte[]? ExtraBits;

	private LzxState mState;

	public LzxDecoder(int window) {
		var wndsize = (uint)(1 << window);
		int posnSlots;

		// Setup proper exception.
		if (window is < 15 or > 21)
		{
			throw new UnsupportedWindowSizeRange();
		}

		// Let's initialize our state.
		mState = new LzxState();
		mState.ActualSize = 0;
		mState.Window = new byte[wndsize];
		for (var i = 0; i < wndsize; i++)
		{
			mState.Window[i] = 0xDC;
		}
		mState.ActualSize = wndsize;
		mState.WindowSize = wndsize;
		mState.WindowPosn = 0;

		// Initialize static tables.
		if (ExtraBits == null) {
			ExtraBits = new byte[52];
			for (int i = 0, j = 0; i <= 50; i += 2) {
				ExtraBits[i] = ExtraBits[i + 1] = (byte)j;
				if (i != 0 && j < 17)
				{
					j++;
				}
			}
		}
		if (PositionBase == null) {
			PositionBase = new uint[51];
			for (int i = 0, j = 0; i <= 50; i++) {
				PositionBase[i] = (uint)j;
				j += 1 << ExtraBits[i];
			}
		}

		// Calculate required position slots.
		if (window == 20)
		{
			posnSlots = 42;
		}
		else if (window == 21)
		{
			posnSlots = 50;
		}
		else
		{
			posnSlots = window << 1;
		}

		mState.R0 = mState.R1 = mState.R2 = 1;
		mState.MainElements = (ushort)(LzxConstants.NUM_CHARS + (posnSlots << 3));
		mState.HeaderRead = 0;
		mState.FramesRead = 0;
		mState.BlockRemaining = 0;
		mState.BlockType = LzxConstants.Blocktype.Invalid;
		mState.IntelCurpos = 0;
		mState.IntelStarted = 0;

		mState.PretreeTable = new ushort[(1 << LzxConstants.PRETREE_TABLEBITS) + (LzxConstants.PRETREE_MAXSYMBOLS << 1)];
		mState.PretreeLen = new byte[LzxConstants.PRETREE_MAXSYMBOLS + LzxConstants.LENTABLE_SAFETY];
		mState.MaintreeTable = new ushort[(1 << LzxConstants.MAINTREE_TABLEBITS) + (LzxConstants.MAINTREE_MAXSYMBOLS << 1)];
		mState.MaintreeLen = new byte[LzxConstants.MAINTREE_MAXSYMBOLS + LzxConstants.LENTABLE_SAFETY];
		mState.LengthTable = new ushort[(1 << LzxConstants.LENGTH_TABLEBITS) + (LzxConstants.LENGTH_MAXSYMBOLS << 1)];
		mState.LengthLen = new byte[LzxConstants.LENGTH_MAXSYMBOLS + LzxConstants.LENTABLE_SAFETY];
		mState.AlignedTable = new ushort[(1 << LzxConstants.ALIGNED_TABLEBITS) + (LzxConstants.ALIGNED_MAXSYMBOLS << 1)];
		mState.AlignedLen = new byte[LzxConstants.ALIGNED_MAXSYMBOLS + LzxConstants.LENTABLE_SAFETY];

		// Initialize tables to 0 (because deltas will be applied to them).
		for (var i = 0; i < LzxConstants.MAINTREE_MAXSYMBOLS; i++)
		{
			mState.MaintreeLen[i] = 0;
		}
		for (var i = 0; i < LzxConstants.LENGTH_MAXSYMBOLS; i++)
		{
			mState.LengthLen[i] = 0;
		}
	}

	public int Decompress(Stream inData, int inLen, Stream outData, int outLen) {
		var bitbuf = new BitBuffer(inData);
		var startpos = inData.Position;
		var endpos = inData.Position + inLen;

		var window = mState.Window;

		var windowPosn = mState.WindowPosn;
		var windowSize = mState.WindowSize;
		var r0 = mState.R0;
		var r1 = mState.R1;
		var r2 = mState.R2;
		uint i, j;

		int togo = outLen, thisRun, mainElement, matchLength, matchOffset, lengthFooter, extra, verbatimBits;
		int rundest, runsrc, copyLength, alignedBits;

		bitbuf.InitBitStream();

		// Read header if necessary.
		if (mState.HeaderRead == 0) {
			var intel = bitbuf.ReadBits(1);
			if (intel != 0) {
				// Read the filesize.
				i = bitbuf.ReadBits(16); j = bitbuf.ReadBits(16);
				mState.IntelFilesize = (int)((i << 16) | j);
			}
			mState.HeaderRead = 1;
		}

		// Main decoding loop.
		while (togo > 0) {
			// last block finished, new block expected.
			if (mState.BlockRemaining == 0) {
				// TODO may screw something up here
				if (mState.BlockType == LzxConstants.Blocktype.Uncompressed) {
					if ((mState.BlockLength & 1) == 1)
					{
						inData.ReadByte(); /* realign bitstream to word */
					}
					bitbuf.InitBitStream();
				}

				mState.BlockType = (LzxConstants.Blocktype)bitbuf.ReadBits(3);
				i = bitbuf.ReadBits(16);
				j = bitbuf.ReadBits(8);
				mState.BlockRemaining = mState.BlockLength = (i << 8) | j;

				switch (mState.BlockType) {
					case LzxConstants.Blocktype.Aligned:
						for (i = 0, j = 0; i < 8; i++) { j = bitbuf.ReadBits(3); mState.AlignedLen[i] = (byte)j; }
						MakeDecodeTable(LzxConstants.ALIGNED_MAXSYMBOLS, LzxConstants.ALIGNED_TABLEBITS,
							mState.AlignedLen, mState.AlignedTable);
						// Rest of aligned header is same as verbatim
						goto case LzxConstants.Blocktype.Verbatim;

					case LzxConstants.Blocktype.Verbatim:
						ReadLengths(mState.MaintreeLen, 0, 256, bitbuf);
						ReadLengths(mState.MaintreeLen, 256, mState.MainElements, bitbuf);
						MakeDecodeTable(LzxConstants.MAINTREE_MAXSYMBOLS, LzxConstants.MAINTREE_TABLEBITS,
							mState.MaintreeLen, mState.MaintreeTable);
						if (mState.MaintreeLen[0xE8] != 0)
						{
							mState.IntelStarted = 1;
						}

						ReadLengths(mState.LengthLen, 0, LzxConstants.NUM_SECONDARY_LENGTHS, bitbuf);
						MakeDecodeTable(LzxConstants.LENGTH_MAXSYMBOLS, LzxConstants.LENGTH_TABLEBITS,
							mState.LengthLen, mState.LengthTable);
						break;

					case LzxConstants.Blocktype.Uncompressed:
						mState.IntelStarted = 1; // Because we can't assume otherwise.
						bitbuf.EnsureBits(16); // Get up to 16 pad bits into the buffer.
						if (bitbuf.GetBitsLeft() > 16)
						{
							inData.Seek(-2, SeekOrigin.Current); /* and align the bitstream! */
						}
						byte hi, mh, ml, lo;
						lo = (byte)inData.ReadByte(); ml = (byte)inData.ReadByte(); mh = (byte)inData.ReadByte(); hi = (byte)inData.ReadByte();
						r0 = (uint)(lo | (ml << 8) | (mh << 16) | (hi << 24));
						lo = (byte)inData.ReadByte(); ml = (byte)inData.ReadByte(); mh = (byte)inData.ReadByte(); hi = (byte)inData.ReadByte();
						r1 = (uint)(lo | (ml << 8) | (mh << 16) | (hi << 24));
						lo = (byte)inData.ReadByte(); ml = (byte)inData.ReadByte(); mh = (byte)inData.ReadByte(); hi = (byte)inData.ReadByte();
						r2 = (uint)(lo | (ml << 8) | (mh << 16) | (hi << 24));
						break;

					default:
						return -1; // TODO throw proper exception
				}
			}

			// Buffer exhaustion check.
			if (inData.Position > startpos + inLen) {
				/* It's possible to have a file where the next run is less than
				 * 16 bits in size. In this case, the READ_HUFFSYM() macro used
				 * in building the tables will exhaust the buffer, so we should
				 * allow for this, but not allow those accidentally read bits to
				 * be used (so we check that there are at least 16 bits
				 * remaining - in this boundary case they aren't really part of
				 * the compressed data).
				 */

				if (inData.Position > startpos + inLen + 2 || bitbuf.GetBitsLeft() < 16)
				{
					return -1; //TODO throw proper exception
				}
			}

			while ((thisRun = (int)mState.BlockRemaining) > 0 && togo > 0) {
				if (thisRun > togo)
				{
					thisRun = togo;
				}
				togo -= thisRun;
				mState.BlockRemaining -= (uint)thisRun;

				// Apply 2^x-1 mask.
				windowPosn &= windowSize - 1;
				// Runs can't straddle the window wraparound.
				if (windowPosn + thisRun > windowSize)
				{
					return -1; // TODO: throw proper exception
				}

				switch (mState.BlockType) {
					case LzxConstants.Blocktype.Verbatim:
						while (thisRun > 0) {
							mainElement = (int)ReadHuffSym(mState.MaintreeTable, mState.MaintreeLen,
								LzxConstants.MAINTREE_MAXSYMBOLS, LzxConstants.MAINTREE_TABLEBITS,
								bitbuf);
							if (mainElement < LzxConstants.NUM_CHARS) {
								// Literal: 0 to NUM_CHARS-1.
								window[windowPosn++] = (byte)mainElement;
								thisRun--;
							}
							else {
								// Match: NUM_CHARS + ((slot<<3) | length_header (3 bits))
								mainElement -= LzxConstants.NUM_CHARS;

								matchLength = mainElement & LzxConstants.NUM_PRIMARY_LENGTHS;
								if (matchLength == LzxConstants.NUM_PRIMARY_LENGTHS) {
									lengthFooter = (int)ReadHuffSym(mState.LengthTable, mState.LengthLen,
										LzxConstants.LENGTH_MAXSYMBOLS, LzxConstants.LENGTH_TABLEBITS,
										bitbuf);
									matchLength += lengthFooter;
								}
								matchLength += LzxConstants.MIN_MATCH;

								matchOffset = mainElement >> 3;

								if (matchOffset > 2) {
									// Not repeated offset.
									if (matchOffset != 3) {
										extra = ExtraBits[matchOffset];
										verbatimBits = (int)bitbuf.ReadBits((byte)extra);
										matchOffset = (int)PositionBase[matchOffset] - 2 + verbatimBits;
									}
									else {
										matchOffset = 1;
									}

									// Update repeated offset LRU queue.
									r2 = r1; r1 = r0; r0 = (uint)matchOffset;
								}
								else if (matchOffset == 0) {
									matchOffset = (int)r0;
								}
								else if (matchOffset == 1) {
									matchOffset = (int)r1;
									r1 = r0; r0 = (uint)matchOffset;
								}
								else // match_offset == 2
								{
									matchOffset = (int)r2;
									r2 = r0; r0 = (uint)matchOffset;
								}

								rundest = (int)windowPosn;
								thisRun -= matchLength;

								// Copy any wrapped around source data
								if (windowPosn >= matchOffset) {
									// No wrap
									runsrc = rundest - matchOffset;
								}
								else {
									runsrc = rundest + ((int)windowSize - matchOffset);
									copyLength = matchOffset - (int)windowPosn;
									if (copyLength < matchLength) {
										matchLength -= copyLength;
										windowPosn += (uint)copyLength;
										while (copyLength-- > 0)
										{
											window[rundest++] = window[runsrc++];
										}
										runsrc = 0;
									}
								}
								windowPosn += (uint)matchLength;

								// Copy match data - no worries about destination wraps
								while (matchLength-- > 0)
								{
									window[rundest++] = window[runsrc++];
								}
							}
						}
						break;

					case LzxConstants.Blocktype.Aligned:
						while (thisRun > 0) {
							mainElement = (int)ReadHuffSym(mState.MaintreeTable, mState.MaintreeLen,
								LzxConstants.MAINTREE_MAXSYMBOLS, LzxConstants.MAINTREE_TABLEBITS,
								bitbuf);

							if (mainElement < LzxConstants.NUM_CHARS) {
								// Literal 0 to NUM_CHARS-1
								window[windowPosn++] = (byte)mainElement;
								thisRun -= 1;
							}
							else {
								// Match: NUM_CHARS + ((slot<<3) | length_header (3 bits))
								mainElement -= LzxConstants.NUM_CHARS;

								matchLength = mainElement & LzxConstants.NUM_PRIMARY_LENGTHS;
								if (matchLength == LzxConstants.NUM_PRIMARY_LENGTHS) {
									lengthFooter = (int)ReadHuffSym(mState.LengthTable, mState.LengthLen,
										LzxConstants.LENGTH_MAXSYMBOLS, LzxConstants.LENGTH_TABLEBITS,
										bitbuf);
									matchLength += lengthFooter;
								}
								matchLength += LzxConstants.MIN_MATCH;

								matchOffset = mainElement >> 3;

								if (matchOffset > 2) {
									// Not repeated offset.
									extra = ExtraBits[matchOffset];
									matchOffset = (int)PositionBase[matchOffset] - 2;
									if (extra > 3) {
										// Verbatim and aligned bits.
										extra -= 3;
										verbatimBits = (int)bitbuf.ReadBits((byte)extra);
										matchOffset += verbatimBits << 3;
										alignedBits = (int)ReadHuffSym(mState.AlignedTable, mState.AlignedLen,
											LzxConstants.ALIGNED_MAXSYMBOLS, LzxConstants.ALIGNED_TABLEBITS,
											bitbuf);
										matchOffset += alignedBits;
									}
									else if (extra == 3) {
										// Aligned bits only.
										alignedBits = (int)ReadHuffSym(mState.AlignedTable, mState.AlignedLen,
											LzxConstants.ALIGNED_MAXSYMBOLS, LzxConstants.ALIGNED_TABLEBITS,
											bitbuf);
										matchOffset += alignedBits;
									}
									else if (extra > 0) // extra==1, extra==2
									{
										// Verbatim bits only.
										verbatimBits = (int)bitbuf.ReadBits((byte)extra);
										matchOffset += verbatimBits;
									}
									else // extra == 0
									{
										// ???
										matchOffset = 1;
									}

									// Update repeated offset LRU queue.
									r2 = r1; r1 = r0; r0 = (uint)matchOffset;
								}
								else if (matchOffset == 0) {
									matchOffset = (int)r0;
								}
								else if (matchOffset == 1) {
									matchOffset = (int)r1;
									r1 = r0; r0 = (uint)matchOffset;
								}
								else // match_offset == 2
								{
									matchOffset = (int)r2;
									r2 = r0; r0 = (uint)matchOffset;
								}

								rundest = (int)windowPosn;
								thisRun -= matchLength;

								// Copy any wrapped around source data
								if (windowPosn >= matchOffset) {
									// No wrap
									runsrc = rundest - matchOffset;
								}
								else {
									runsrc = rundest + ((int)windowSize - matchOffset);
									copyLength = matchOffset - (int)windowPosn;
									if (copyLength < matchLength) {
										matchLength -= copyLength;
										windowPosn += (uint)copyLength;
										while (copyLength-- > 0)
										{
											window[rundest++] = window[runsrc++];
										}
										runsrc = 0;
									}
								}
								windowPosn += (uint)matchLength;

								// Copy match data - no worries about destination wraps.
								while (matchLength-- > 0)
								{
									window[rundest++] = window[runsrc++];
								}
							}
						}
						break;

					case LzxConstants.Blocktype.Uncompressed:
						if (inData.Position + thisRun > endpos)
						{
							return -1; // TODO: Throw proper exception
						}
						var tempBuffer = new byte[thisRun];
						inData.Read(tempBuffer, 0, thisRun);
						tempBuffer.CopyTo(window, (int)windowPosn);
						windowPosn += (uint)thisRun;
						break;

					default:
						return -1; // TODO: Throw proper exception
				}
			}
		}

		if (togo != 0)
		{
			return -1; // TODO: Throw proper exception
		}
		var startWindowPos = (int)windowPosn;
		if (startWindowPos == 0)
		{
			startWindowPos = (int)windowSize;
		}
		startWindowPos -= outLen;
		outData.Write(window, startWindowPos, outLen);

		mState.WindowPosn = windowPosn;
		mState.R0 = r0;
		mState.R1 = r1;
		mState.R2 = r2;

		// TODO: Finish intel E8 decoding.
		// Intel E8 decoding.
		if (mState.FramesRead++ < 32768 && mState.IntelFilesize != 0) {
			if (outLen <= 6 || mState.IntelStarted == 0) {
				mState.IntelCurpos += outLen;
			}
			else {
				var dataend = outLen - 10;
				var curpos = (uint)mState.IntelCurpos;

				mState.IntelCurpos = (int)curpos + outLen;

				while (outData.Position < dataend) {
					if (outData.ReadByte() != 0xE8) { curpos++;
					}
				}
			}
			return -1;
		}
		return 0;
	}

	// TODO: Make returns throw exceptions
	private int MakeDecodeTable(uint nsyms, uint nbits, byte[] length, ushort[] table) {
		ushort sym;
		uint leaf;
		byte bitNum = 1;
		uint fill;
		uint pos = 0; // The current position in the decode table.
		var tableMask = (uint)(1 << (int)nbits);
		var bitMask = tableMask >> 1; // Don't do 0 length codes.
		var nextSymbol = bitMask;    // Base of allocation for long codes.

		// Fill entries for codes short enough for a direct mapping.
		while (bitNum <= nbits) {
			for (sym = 0; sym < nsyms; sym++) {
				if (length[sym] == bitNum) {
					leaf = pos;

					if ((pos += bitMask) > tableMask) {
						return 1; // Table overrun
					}

					/* Fill all possible lookups of this symbol with the
					 * symbol itself.
					 */
					fill = bitMask;
					while (fill-- > 0)
					{
						table[leaf++] = sym;
					}
				}
			}
			bitMask >>= 1;
			bitNum++;
		}

		// If there are any codes longer than nbits
		if (pos != tableMask) {
			// Clear the remainder of the table.
			for (sym = (ushort)pos; sym < tableMask; sym++)
			{
				table[sym] = 0;
			}

			// Give ourselves room for codes to grow by up to 16 more bits.
			pos <<= 16;
			tableMask <<= 16;
			bitMask = 1 << 15;

			while (bitNum <= 16) {
				for (sym = 0; sym < nsyms; sym++) {
					if (length[sym] == bitNum) {
						leaf = pos >> 16;
						for (fill = 0; fill < bitNum - nbits; fill++) {
							// if this path hasn't been taken yet, 'allocate' two entries.
							if (table[leaf] == 0) {
								table[nextSymbol << 1] = 0;
								table[(nextSymbol << 1) + 1] = 0;
								table[leaf] = (ushort)nextSymbol++;
							}
							// Follow the path and select either left or right for next bit.
							leaf = (uint)(table[leaf] << 1);
							if (((pos >> (int)(15 - fill)) & 1) == 1)
							{
								leaf++;
							}
						}
						table[leaf] = sym;

						if ((pos += bitMask) > tableMask)
						{
							return 1;
						}
					}
				}
				bitMask >>= 1;
				bitNum++;
			}
		}

		// full table?
		if (pos == tableMask)
		{
			return 0;
		}

		// Either erroneous table, or all elements are 0 - let's find out.
		for (sym = 0; sym < nsyms; sym++)
		{
			if (length[sym] != 0)
			{
				return 1;
			}
		}
		return 0;
	}

	// TODO:  Throw exceptions instead of returns
	private void ReadLengths(byte[] lens, uint first, uint last, BitBuffer bitbuf) {
		uint x, y;
		int z;

		// hufftbl pointer here?

		for (x = 0; x < 20; x++) {
			y = bitbuf.ReadBits(4);
			mState.PretreeLen[x] = (byte)y;
		}
		MakeDecodeTable(LzxConstants.PRETREE_MAXSYMBOLS, LzxConstants.PRETREE_TABLEBITS,
			mState.PretreeLen, mState.PretreeTable);

		for (x = first; x < last;) {
			z = (int)ReadHuffSym(mState.PretreeTable, mState.PretreeLen,
				LzxConstants.PRETREE_MAXSYMBOLS, LzxConstants.PRETREE_TABLEBITS, bitbuf);
			if (z == 17) {
				y = bitbuf.ReadBits(4); y += 4;
				while (y-- != 0)
				{
					lens[x++] = 0;
				}
			}
			else if (z == 18) {
				y = bitbuf.ReadBits(5); y += 20;
				while (y-- != 0)
				{
					lens[x++] = 0;
				}
			}
			else if (z == 19) {
				y = bitbuf.ReadBits(1); y += 4;
				z = (int)ReadHuffSym(mState.PretreeTable, mState.PretreeLen,
					LzxConstants.PRETREE_MAXSYMBOLS, LzxConstants.PRETREE_TABLEBITS, bitbuf);
				z = lens[x] - z; if (z < 0)
				{
					z += 17;
				}
				while (y-- != 0)
				{
					lens[x++] = (byte)z;
				}
			}
			else {
				z = lens[x] - z; if (z < 0)
				{
					z += 17;
				}
				lens[x++] = (byte)z;
			}
		}
	}

	private uint ReadHuffSym(ushort[] table, byte[] lengths, uint nsyms, uint nbits, BitBuffer bitbuf) {
		uint i, j;
		bitbuf.EnsureBits(16);
		if ((i = table[bitbuf.PeekBits((byte)nbits)]) >= nsyms) {
			j = (uint)(1 << (int)(sizeof(uint) * 8 - nbits));
			do {
				j >>= 1; i <<= 1; i |= (bitbuf.GetBuffer() & j) != 0 ? (uint)1 : 0;
				if (j == 0)
				{
					return 0; // TODO: throw proper exception
				}
			} while ((i = table[i]) >= nsyms);
		}
		j = lengths[i];
		bitbuf.RemoveBits((byte)j);

		return i;
	}

#region Our BitBuffer Class
	private class BitBuffer {
		private uint buffer;
		private byte bitsleft;
		private readonly Stream byteStream;

		public BitBuffer(Stream stream) {
			byteStream = stream;
			InitBitStream();
		}

		public void InitBitStream() {
			buffer = 0;
			bitsleft = 0;
		}

		public void EnsureBits(byte bits) {
			while (bitsleft < bits) {
				int lo = (byte)byteStream.ReadByte();
				int hi = (byte)byteStream.ReadByte();
				buffer |= (uint)(((hi << 8) | lo) << (sizeof(uint) * 8 - 16 - bitsleft));
				bitsleft += 16;
			}
		}

		public uint PeekBits(byte bits) {
			return buffer >> (sizeof(uint) * 8 - bits);
		}

		public void RemoveBits(byte bits) {
			buffer <<= bits;
			bitsleft -= bits;
		}

		public uint ReadBits(byte bits) {
			uint ret = 0;

			if (bits > 0) {
				EnsureBits(bits);
				ret = PeekBits(bits);
				RemoveBits(bits);
			}

			return ret;
		}

		public uint GetBuffer() {
			return buffer;
		}

		public byte GetBitsLeft() {
			return bitsleft;
		}
	}
#endregion

	private struct LzxState {
		public uint R0, R1, R2; // For the LRU offset system
		public ushort MainElements; // Number of main tree elements
		public int HeaderRead; // Have we started decoding at all yet?
		public LzxConstants.Blocktype BlockType; // Type of this block
		public uint BlockLength; // Uncompressed length of this block
		public uint BlockRemaining; // Uncompressed bytes still left to decode
		public uint FramesRead; // The number of CFDATA blocks processed
		public int IntelFilesize; // Magic header value used for transform
		public int IntelCurpos; // Current offset in transform space
		public int IntelStarted; // Have we seen any translateable data yet?

		public ushort[] PretreeTable;
		public byte[] PretreeLen;
		public ushort[] MaintreeTable;
		public byte[] MaintreeLen;
		public ushort[] LengthTable;
		public byte[] LengthLen;
		public ushort[] AlignedTable;
		public byte[] AlignedLen;

		/* NEEDED MEMBERS
		 * CAB actualsize
		 * CAB window
		 * CAB window_size
		 * CAB window_posn
		 */
		public uint ActualSize;
		public byte[] Window;
		public uint WindowSize;
		public uint WindowPosn;
	}
}

// CONSTANTS
internal struct LzxConstants {
	public const ushort MIN_MATCH = 2;
	public const ushort MAX_MATCH = 257;
	public const ushort NUM_CHARS = 256;
	public enum Blocktype {
		Invalid = 0,
		Verbatim = 1,
		Aligned = 2,
		Uncompressed = 3,
	}
	public const ushort PRETREE_NUM_ELEMENTS = 20;
	public const ushort ALIGNED_NUM_ELEMENTS = 8;
	public const ushort NUM_PRIMARY_LENGTHS = 7;
	public const ushort NUM_SECONDARY_LENGTHS = 249;

	public const ushort PRETREE_MAXSYMBOLS = PRETREE_NUM_ELEMENTS;
	public const ushort PRETREE_TABLEBITS = 6;
	public const ushort MAINTREE_MAXSYMBOLS = NUM_CHARS + 50 * 8;
	public const ushort MAINTREE_TABLEBITS = 12;
	public const ushort LENGTH_MAXSYMBOLS = NUM_SECONDARY_LENGTHS + 1;
	public const ushort LENGTH_TABLEBITS = 12;
	public const ushort ALIGNED_MAXSYMBOLS = ALIGNED_NUM_ELEMENTS;
	public const ushort ALIGNED_TABLEBITS = 7;

	public const ushort LENTABLE_SAFETY = 64;
}

// EXCEPTIONS
internal class UnsupportedWindowSizeRange : Exception {
}