﻿using System;
using System.Xml.Linq;
using System.Collections.Generic;
using KKdMainLib.IO;
using KKdMainLib.MessagePack;
using MPIO = KKdMainLib.MessagePack.IO;

namespace KKdMainLib
{
    public class STR
    {
        public struct String
        {
            public int ID;
            public int StrOffset;
            public string Str;
        }

        public STR()
        { Count = 0; Offset = 0; OffsetX = 0; STRs = null; POF = null; Header = new PDHead(); }

        public long Count;
        private long Offset;
        private long OffsetX;
        public String[] STRs;
        private POF POF;
        private PDHead Header;

        public int STRReader(string filepath, string ext)
        {
            Stream reader =  File.OpenReader(filepath + ext);

            Header = new PDHead();
            reader.Format = Main.Format.F;
            Header.Signature = reader.ReadInt32();
            if (Header.Signature == 0x41525453)
            {
                Header = reader.ReadHeader(true);
                POF = Header.AddPOF();
                reader.Position = Header.Lenght;
                
                Count = reader.ReadInt32Endian();
                Offset = reader.ReadInt32Endian();
                
                if (Offset == 0)
                {
                    Offset = Count;
                    OffsetX = reader.ReadInt64();
                    Count   = reader.ReadInt64();
                    reader.Offset = Header.Lenght;
                    reader.Format = Main.Format.X;
                }
                reader.LongPosition = reader.IsX ? Offset + reader.Offset : Offset;

                STRs = new String[Count];
                for (int i = 0; i < Count; i++)
                {
                    STRs[i].StrOffset = reader.GetOffset(ref POF).ReadInt32Endian();
                    STRs[i].ID        = reader.ReadInt32Endian();
                    if (reader.IsX) STRs[i].StrOffset += (int)OffsetX;
                }

                for (int i = 0; i < Count; i++)
                {
                    reader.LongPosition = STRs[i].StrOffset + (reader.IsX ? reader.Offset : 0);
                    STRs[i].Str = reader.NullTerminatedUTF8();
                }

                reader.Position = POF.Offset;
                reader.ReadPOF(ref POF);
            }
            else
            {
                reader.Position -= 4;
                Count = 0;
                for (int a = 0, i = 0; reader.Position > 0 && reader.Position < reader.Length; i++, Count++)
                {
                    a = reader.ReadInt32();
                    if (a == 0) break;
                }
                STRs = new String[Count];

                for (int i = 0; i < Count; i++)
                {
                    reader.LongPosition = STRs[i].StrOffset + (reader.IsX ? reader.Offset : 0);
                    STRs[i].ID  = i;
                    STRs[i].Str = reader.NullTerminatedUTF8();
                }
            }

            reader.Close();
            return 1;
        }

        public void STRWriter(string filepath)
        {
            uint Offset = 0;
            uint CurrentOffset = 0;
            Stream writer = File.OpenWriter(filepath + (Header.
                Format > Main.Format.FT ? ".str" : ".bin"), true);
            writer.Format = Header.Format;
            POF = new POF();
            writer.IsBE = writer.Format == Main.Format.F2BE;

            if (writer.Format > Main.Format.FT)
            {
                writer.Position = 0x40;
                writer.WriteEndian(Count);
                writer.GetOffset(ref POF).WriteEndian(0x80);
                writer.Position = 0x80;
                for (int i = 0; i < Count; i++)
                {
                    writer.GetOffset(ref POF).Write(0x00);
                    writer.WriteEndian(STRs[i].ID);
                }
                writer.Align(16);
            }
            else
            {
                for (int i = 0; i < Count; i++)
                    writer.Write(0x00);
                writer.Align(32);
            }

            List<string> UsedSTR = new List<string>();
            List<int> UsedSTRPos = new List<int>();
            int[] STRPos = new int[Count];
            for (int i1 = 0; i1 < Count; i1++)
            {
                if (UsedSTR.Contains(STRs[i1].Str))
                {
                    for (int i2 = 0; i2 < Count; i2++)
                        if (UsedSTR[i2] == STRs[i1].Str)
                        { STRPos[i1] = UsedSTRPos[i2]; break; }
                }
                else
                {
                    STRPos[i1] = writer.Position;
                    UsedSTRPos.Add(STRPos[i1]);
                    UsedSTR.Add(STRs[i1].Str);
                    writer.Write(STRs[i1].Str);
                    writer.WriteByte(0);
                }
            }
            if (writer.Format > Main.Format.FT)
            {
                writer.Align(16);
                Offset = writer.UIntPosition;
                writer.Position = 0x80;
            }
            else
                writer.Position = 0;
            for (int i1 = 0; i1 < Count; i1++)
            {
                writer.WriteEndian(STRPos[i1]);
                if (writer.Format > Main.Format.FT) writer.Position += 4;
            }

            if (writer.Format > Main.Format.FT)
            {
                writer.UIntPosition = Offset;
                writer.Write(ref POF, 1);
                CurrentOffset = writer.UIntPosition;
                writer.WriteEOFC(0);
                Header.Lenght = 0x40;
                Header.DataSize = (int)(CurrentOffset - Header.Lenght);
                Header.Signature = 0x41525453;
                Header.SectionSize = (int)(Offset - Header.Lenght);
                writer.Position = 0;
                writer.Write(Header);
            }
            writer.Close();
        }

        public void MsgPackReader(string filepath)
        {
            //STRs = new List<String>();

            Xml Xml = new Xml();
            Xml.OpenXml(filepath + ".xml", true);
            Xml.Compact = true;
            int i = 0;
            foreach (XElement STR_ in Xml.doc.Elements("STR"))
            {
                foreach (XAttribute Entry in STR_.Attributes())
                    if (Entry.Name == "Format")
                        Enum.TryParse(Entry.Value, out Header.Format);
                foreach (XElement STREntry in STR_.Elements())
                {
                    if (STREntry.Name == "STREntry")
                    {
                        String Str = new String();
                        foreach (XAttribute Entry in STREntry.Attributes())
                        {
                            if (Entry.Name == "ID"    ) Str.ID  = int.Parse(Entry.Value);
                            if (Entry.Name == "String") Str.Str =           Entry.Value ;
                        }
                        //STRs.Add(Str);
                    }
                    i++;
                }
            }
            Count = i;
        }

        public void MsgPackWriter(string filepath)
        {
            MsgPack STR_ = new MsgPack("STR").Add("Format", Header.Format.ToString()).Add("Count", Count);
            MsgPack Strings = new MsgPack("Strings", Count);
            for (int i = 0; i < Count; i++)
            {
                Strings[i] = new MsgPack().Add("ID", STRs[i].ID);
                if (STRs[i].Str != null)
                    if (STRs[i].Str != "")
                        ((MsgPack)Strings[i]).Add("S", STRs[i].Str); ;
            }
            STR_.Add(Strings);

            MsgPack MsgPack = new MsgPack(MsgPack.Types.FixMap).Add(STR_);

            MPIO IO = new MPIO(File.OpenWriter(filepath + ".mp", true));
            IO.Write(MsgPack, true);
            IO = null;
            MsgPack = null;
        }
    }
}
