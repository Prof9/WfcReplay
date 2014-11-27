// warning: god-awful code below

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WfcReplay
{
	class Program
	{
		static string version = "v0.6";

		static void Main(string[] args)
		{
			Console.WriteLine("WfcReplay " + version + " by Prof. 9");
			Console.WriteLine();

			if (args.Length < 1)
			{
				Console.WriteLine("Usage: WfcReplay.exe rompath");
			}
			else
			{
#if !DEBUG
				try
#endif
				{
					bool checkMode = false;
					bool local = false;
					bool manualMode = false;

					int argStart = 0;
					bool flagsLeft = true;
					while (flagsLeft)
					{
						switch (args[argStart++])
						{
							case "--check":
							case "-c":
								checkMode = true;
								break;
							case "--local":
							case "-l":
								local = true;
								break;
							case "--manual":
							case "-m":
								manualMode = true;
								break;
							default:
								argStart--;
								flagsLeft = false;
								break;
						}
					}

					string romPath = args[argStart];

					Console.WriteLine("Loading ROM...");
					Stream rom = readFile(romPath);
					BinaryReader romReader = new BinaryReader(rom);
					string code = new Program(checkMode, local, manualMode).process(romReader);
					Console.WriteLine("Finished analyzing ROM.");
					
					if (code != null)
					{
						MemoryStream outStream = new MemoryStream(Encoding.UTF8.GetBytes(code));
						outStream.Position = 0;
						string fileName = makeGameString(romReader) + ".txt";
						fileName = makeFileNameSafe(fileName);
						writeFile(fileName, outStream);
						Console.WriteLine("Success!");
						Console.WriteLine("Code written to " + Directory.GetCurrentDirectory() + "\\" + fileName + ".");
					}
					else if (!checkMode)
					{
						Console.WriteLine("No HTTPS URLs to patch were found.");
					}
				}
#if !DEBUG
				catch (Exception ex)
				{
					Console.WriteLine("FATAL ERROR: " + ex.Message);
				}
#endif
#if DEBUG
				Console.ReadKey();
#endif
			}
		}

		static MemoryStream readFile(string path)
		{
			if (!File.Exists(path))
			{
				throw new IOException("Could not find file " + path + ".");
			}

			FileStream fs = null;
			MemoryStream mem = new MemoryStream();
			try
			{
				fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				fs.CopyTo(mem);
			}
			catch (Exception)
			{
				throw new IOException("Could not open file " + path + " for reading.");
			}
			finally
			{
				if (fs != null)
				{
					fs.Close();
				}
			}

			return mem;
		}
		static void writeFile(string fileName, Stream file)
		{
			FileStream fs = null;
			try
			{
				fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None);
				file.CopyTo(fs);
			}
			catch
			{
				throw new IOException("Could not write file to disk.");
			}
			finally
			{
				if (fs != null)
				{
					fs.Close();
				}
			}
		}
		MemoryStream readTempFile(string fileName)
		{
			return readFile(tempFolderPath + fileName);
		}
		void writeTempFile(string fileName, Stream file)
		{
			writeFile(tempFolderPath + fileName, file);
		}
		static string makeFileNameSafe(string fileName)
		{
			string[] validParts = fileName.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries);
			return string.Join("-", validParts);
		}

		string tempFolderPath;
		bool checkMode;
		bool manualMode;

		Program(bool checkMode, bool local, bool manualMode)
		{
			this.checkMode = checkMode;
			this.manualMode = manualMode;

			try
			{
#if DEBUG
				if (true)
#else
				if (local)
#endif
				{
					tempFolderPath = @"temp\";
					if (Directory.Exists(tempFolderPath))
					{
						try
						{
							Directory.Delete(tempFolderPath, true);
						}
						catch { }
					}
				}
				else
				{
					tempFolderPath = Path.GetTempPath() + @"WfcReplay\";
				}
				tempFolderPath = Directory.CreateDirectory(tempFolderPath).FullName;
			}
			catch
			{
				throw new IOException("Could not create temporary folder.");
			}
		}
		~Program()
		{
#if !DEBUG
			try
			{
				Directory.Delete(tempFolderPath, true);
			}
			catch { }
#endif
		}

		string process(BinaryReader romReader)
		{
			string gameString = makeGameString(romReader);
			Console.WriteLine("Processing " + gameString + ".");

			uint arm9RamAddress = getArm9RamAddress(romReader);
			string arm9Name = "arm9.bin";

			// Get arm9
			MemoryStream arm9 = getArm9(romReader);
			arm9.Position = 0;
			decryptBlz(ref arm9, arm9Name);
			BinaryReader arm9Reader = new BinaryReader(arm9);

			// Find hook for arm9
			arm9.Position = 0;
			long hookAddress = findHookRomAddress(arm9Reader, arm9RamAddress);
			Console.WriteLine("ARM9 hook found: 0x" + hookAddress.ToString("X8"));
			Console.WriteLine();

			// Search arm9 for URLs
			log("Searching " + arm9Name + "...");
			List<uint> urlAddresses = searchFileForUrls(arm9Reader, arm9RamAddress);
			if (this.checkMode && urlAddresses.Count > 0)
			{
				return true.ToString();
			}

			// Search ARM9 overlays for URLs
			int ovlCount = getOverlay9Count(romReader);
			for (int ovlIndex = 0; ovlIndex < ovlCount; ovlIndex++)
			{
				string ovlName = "ovl9_" + ovlIndex.ToString("X4") + ".bin";
				log("[" + (ovlIndex * 100 / ovlCount).ToString().PadLeft(2, ' ') + "%] Searching " + ovlName + "...");

				uint ramStart = getOverlay9RamAddress(romReader, ovlIndex);

				MemoryStream ovl = getOverlay9(romReader, ovlIndex);
				decryptBlz(ref ovl, ovlName);
				BinaryReader ovlReader = new BinaryReader(ovl);

				ovl.Position = 0;
				urlAddresses.AddRange(searchFileForUrls(ovlReader, ramStart));
				if (this.checkMode && urlAddresses.Count > 0)
				{
					return true.ToString();
				}
			}

			urlAddresses = urlAddresses.Distinct().ToList();
			urlAddresses.Sort();

			if (urlAddresses.Count > 0)
			{
				// Find largest code cave
				arm9.Position = 0;
				List<uint> codeCave = findCodeCave(arm9Reader, (uint)arm9RamAddress);

				// Form code
				string code = "::Bypass HTTPS " + version + " for " + gameString + "\r\n";
				code += makeCode((uint)hookAddress, urlAddresses, codeCave, this.manualMode);
				return code;
			}
			else
			{
				return null;
			}
		}
		void log(string s)
		{
			Console.WriteLine(s);
		}

		static string makeGameString(BinaryReader romReader)
		{
			string s = "[" + getGameCode(romReader) + "]";
			s += " " + getGameTitle(romReader);
			int romVersion = getRomVersion(romReader);
			if (romVersion > 0)
			{
				s += " Rev" + (char)('A' + romVersion - 1);
			}
			return s;
		}
		static string getGameCode(BinaryReader romReader)
		{
			romReader.BaseStream.Position = 0xC;
			return new string(romReader.ReadChars(4));
		}
		static string getGameTitle(BinaryReader romReader)
		{
			romReader.BaseStream.Position = 0x0;
			char[] chars = romReader.ReadChars(12);
			int length = chars.ToList().IndexOf('\0');
			length = length == -1 ? 12 : length;
			return new string(chars, 0, length);
		}
		static int getRomVersion(BinaryReader romReader)
		{
			romReader.BaseStream.Position = 0x1E;
			return romReader.ReadByte();
		}

		static uint getArm9RamAddress(BinaryReader romReader)
		{
			romReader.BaseStream.Position = 0x28;
			return romReader.ReadUInt32();
		}
		static MemoryStream getArm9(BinaryReader romReader)
		{
			romReader.BaseStream.Position = 0x20;
			long pos = romReader.ReadUInt32();
			romReader.BaseStream.Position = 0x2C;
			int size = romReader.ReadInt32();

			romReader.BaseStream.Position = pos;
			MemoryStream arm9 = new MemoryStream(romReader.ReadBytes(size));

			return arm9;
		}
		static uint findHookRomAddress(BinaryReader arm9Reader, uint ramStart)
		{
			while (arm9Reader.BaseStream.Position <= arm9Reader.BaseStream.Length - 0xC)
			{
				long pos = arm9Reader.BaseStream.Position;
				if (arm9Reader.ReadUInt32() == 0xE3A00000 &&	// mov r0,0h
					arm9Reader.ReadUInt32() == 0xEE070F90 &&	// mov p15,0,c7,c0,4,r0 ;Wait For Interrupt
					arm9Reader.ReadUInt32() == 0xE12FFF1E)		// bx r14
				{
					return (uint)(pos + ramStart);
				}
				arm9Reader.BaseStream.Position = pos + 4;
			}
			throw new Exception("Could not find ARM9 hook!");
		}

		static int getOverlay9Count(BinaryReader romReader)
		{
			romReader.BaseStream.Position = 0x54;
			return (int)(romReader.ReadUInt32() / 0x20);
		}
		static uint getOverlay9RamAddress(BinaryReader romReader, int overlayIndex)
		{
			romReader.BaseStream.Position = 0x50;
			romReader.BaseStream.Position = romReader.ReadUInt32();

			romReader.BaseStream.Position += overlayIndex * 0x20;

			romReader.BaseStream.Position += 0x4;
			return romReader.ReadUInt32();
		}
		static MemoryStream getOverlay9(BinaryReader romReader, int overlayIndex)
		{
			romReader.BaseStream.Position = 0x50;
			romReader.BaseStream.Position = romReader.ReadUInt32();

			romReader.BaseStream.Position += overlayIndex * 0x20;

			romReader.BaseStream.Position += 0x18;
			long fileId = romReader.ReadUInt32();

			romReader.BaseStream.Position = 0x48;
			romReader.BaseStream.Position = romReader.ReadUInt32();

			romReader.BaseStream.Position += fileId * 8;

			long pos = romReader.ReadUInt32();
			long size = romReader.ReadUInt32() - pos;

			romReader.BaseStream.Position = pos;
			return new MemoryStream(romReader.ReadBytes((int)size));
		}

		void decryptBlz(ref MemoryStream file, string fileName)
		{
			writeTempFile(fileName, file);

			ProcessStartInfo psi = new ProcessStartInfo("blz.exe",  "-d " + "\"" + tempFolderPath + fileName + "\"");
			psi.WorkingDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
			psi.WindowStyle = ProcessWindowStyle.Hidden;
			Process blz = Process.Start(psi);
			blz.WaitForExit();
			
			file = readTempFile(fileName);
		}

		List<uint> searchFileForUrls(BinaryReader reader, uint ramStart)
		{
			List<uint> result = new List<uint>();

			while (reader.BaseStream.Position <= reader.BaseStream.Length - 9)
			{
				long pos = reader.BaseStream.Position;
				if (reader.ReadByte() == 0x68 &&	// 'h'
					reader.ReadByte() == 0x74 &&	// 't'
					reader.ReadByte() == 0x74 &&	// 't'
					reader.ReadByte() == 0x70 &&	// 'p'
					reader.ReadByte() == 0x73 &&	// 's'
					reader.ReadByte() == 0x3A &&	// ':'
					reader.ReadByte() == 0x2F &&	// '/'
					reader.ReadByte() == 0x2F &&	// '/'
					reader.ReadByte() != 0x00)		// '\0'
				{
					uint addr = (uint)(pos + ramStart);
					result.Add(addr);
					log("\tFound URL at 0x" + addr.ToString("X8"));
				}
				reader.BaseStream.Position = pos + 4;
			}

			return result;
		}

		static List<uint> findCodeCave(BinaryReader arm9Reader, uint arm9RamAddress)
		{
			List<uint> cave = new List<uint>() { 0x2000000, 0x0 };
			//List<uint> cave = new List<uint>() { 0x23FEE00, 0x200 };

			// Start at +0x10
			arm9Reader.BaseStream.Position += 0x10;
			long start = arm9Reader.BaseStream.Position;
			long length;
			while (arm9Reader.BaseStream.Position < 0x800)
			{
				ushort next = arm9Reader.ReadUInt16();
				// if valid SWI
				if ((next & 0xFF00) == 0xDF00 && (next & 0xFF) < 0x20)
				{
					// Get next two opcodes
					ushort next2 = arm9Reader.ReadUInt16();
					ushort next3 = arm9Reader.ReadUInt16();
					// If either are bx r14, end code cave
					if (next2 == 0x4770 || next3 == 0x4770)
					{
						length = arm9Reader.BaseStream.Position - start - 6;
						if (length > cave[1])
						{
							cave = new List<uint>() { (uint)start + arm9RamAddress, (uint)length };
						}
						if (next2 == 0x4770)
						{
							arm9Reader.BaseStream.Position -= 2;
						}
						arm9Reader.BaseStream.Position = (arm9Reader.BaseStream.Position + 3) & ~0x3;
						start = arm9Reader.BaseStream.Position;
					}
					else
					{
						arm9Reader.BaseStream.Position -= 4;
					}
				}
			}
			length = arm9Reader.BaseStream.Position - start;
			if (length > cave[1])
			{
				cave = new List<uint>() { (uint)start + arm9RamAddress, (uint)length };
			}

			return cave;
		}

		static string makeCode(uint arm9Hook, List<uint> urlAddresses, List<uint> codeCave, bool manualMode)
		{
			string code = "";
			urlAddresses.Sort();

			if (urlAddresses.Count > 0)
			{
				List<uint> codeParts = new List<uint>();
				// Hook check
				if (!manualMode) {
					codeParts.Add(0x50000000 | ((arm9Hook + 4) & 0xFFFFFFF));
					codeParts.Add(0xEE070F90);
				}

				List<ushort> caveParts = new List<ushort>();
				//	.thumb
				//	fspace:
				if (manualMode) {
					//	mov		r0,0h
					caveParts.Add(0x2000);
				}
				//		add		r2,=addr			// get array start
				caveParts.Add(0xA20D);
				//		ldmia	[r2]!,r1			// get first string ptr, increment array ptr
				caveParts.Add(0xCA02);
				//	main_loop:
				//		ldr		r3,=2F2F3A73h		// "s://" and negative offset
				caveParts.Add(0x4B0B);
				//		ldr		r4,[r1,r3]			// get second four bytes
				caveParts.Add(0x58CC);
				//		cmp		r4,r3				// should be "s://"
				caveParts.Add(0x429C);
				//		bne		next				// if not "s://", calc next string ptr
				caveParts.Add(0xD105);
				//		add		r3,r3,r1			// calc actual string ptr
				caveParts.Add(0x185B);
				//	patch_loop:
				//		ldrb	r4,[r3,1h]			// get next char
				caveParts.Add(0x785C);
				//		strb	r4,[r3]				// write to current
				caveParts.Add(0x701C);
				//		add		r3,1h				// increment string ptr
				caveParts.Add(0x3301);
				//		tst		r4,r4				// check for zero byte
				caveParts.Add(0x4224);
				//		bne		patch_loop			// if not zero, loop
				caveParts.Add(0xD1FA);
				//	next:							// r0 is zero at this point
				//		ldsh	r4,[r2,r0]			// get next value (signed)
				//									// if positive, offset of next ptr
				//									// if negative, upper bits of next ptr
				//									// if zero, terminator
				caveParts.Add(0x5E14);
				//		lsl		r4,r4,2h			// expand and update flags
				caveParts.Add(0x00A4);
				//		beq		end					// if offset is zero, stop
				caveParts.Add(0xD005);
				//	next_add:
				//		add		r2,2h				// increment array ptr
				caveParts.Add(0x3202);
				//		add		r1,r1,r4			// add offset to string ptr
				caveParts.Add(0x1909);
				//		bcc		main_loop			// if offset was positive, check next
				caveParts.Add(0xD3EF);
				//		lsl		r1,r4,0Eh			// shift to upper and treat as ptr
				caveParts.Add(0x03A1);
				//		ldrh	r4,[r2]				// treat lower as offset
				caveParts.Add(0x8814);
				//		b		next_add			// add offset to pointer
				caveParts.Add(0xE7F9);
				//	end:
				if (!manualMode) {
					//	bx		r15					// switch to ARM
					caveParts.Add(0x4778);
					//	.arm							// r0 is still zero
					//	mov		p15,0,c7,c0,4,r0	// wait for interrupt
					caveParts.Add(0x0F90);
					caveParts.Add(0xEE07);
					//	pop		r1-r4,r15			// pop registers and return
					caveParts.Add(0x801E);
					caveParts.Add(0xE8BD);
				} else {
					//	ldr		r0,[return_addr]
					caveParts.Add(0x4800);
					//	bx		r0
					caveParts.Add(0x4700);
					//	return_addr:
					//	dd		0xAAAAAAAA
					caveParts.Add(0xAAAA);
					caveParts.Add(0xAAAA);
				}
				//	.pool
				caveParts.Add(0x3A73);
				caveParts.Add(0x2F2F);

				List<ushort> dataParts = new List<ushort>();
				for (int i = 0; i < urlAddresses.Count; i++)
				{
					uint ptr = urlAddresses[i] - 0x2F2F3A73 + 4;
					ushort upper = (ushort)(ptr >> 16);
					ushort lower = (ushort)(ptr & 0xFFFF);
					if (i == 0)
					{
						dataParts.Add(lower);
						dataParts.Add(upper);
					}
					else if (urlAddresses[i] - urlAddresses[i - 1] < 0x20000)
					{
						dataParts.Add((ushort)((urlAddresses[i] - urlAddresses[i - 1]) >> 2));
					}
					else
					{
						dataParts.Add(upper);
						dataParts.Add(lower);
					}
				}
				dataParts.Add(0x0000);
				caveParts.AddRange(dataParts);

				if (codeCave[1] < caveParts.Count * 2)
				{
					throw new IOException("Could not find a suitable code cave.");
				}

				codeParts.Add(0xE0000000 | (codeCave[0] & 0xFFFFFFF));
				codeParts.Add((uint)(caveParts.Count * 2));

				if (caveParts.Count % 2 != 0)
				{
					caveParts.Add(0x0000);
				}
				for (int i = 0; i < caveParts.Count; i += 2)
				{
					codeParts.Add((uint)(caveParts[i] + (caveParts[i + 1] << 16)));
				}

				if (codeParts.Count % 2 == 1)
				{
					codeParts.Add(0x00000000);
				}

				if (!manualMode) {
					codeParts.Add((arm9Hook + 4) & 0xFFFFFFF);
					//		push	r1-r4,r14					// push registers
					codeParts.Add(0xE92D401E);
					codeParts.Add((arm9Hook + 8) & 0xFFFFFFF);
					//		blx		fspace						// branch to code cave
					uint opcode = 0xFA000000;
					opcode |= (uint)((codeCave[0] & 2) != 0 ? 0x01000000 : 0x00000000);
					opcode |= (uint)(-(((arm9Hook - codeCave[0]) / 4) + 4) & 0xFFFFFF);
					codeParts.Add(opcode);

					codeParts.Add(0xD2000000);
					codeParts.Add(0x00000000);
				}

				bool first = true;
				foreach (uint part in codeParts)
				{
					if (first)
					{
						code += "\r\n";
					}
					else
					{
						code += " ";
					}
					code += part.ToString("X8");
					first = !first;
				}
				if (!first)
				{
					code += " 00000000";
				}
				code = code.Substring(2);
			}

			return code;
		}
	}
}