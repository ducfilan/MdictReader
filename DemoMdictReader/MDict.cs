//https://dotnetzip.codeplex.com
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Ionic.Zlib;

namespace DemoMdictReader
{
    public class MDict
    {
        public string Filename { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public int NumberWidth { get; set; }
        public long KeyListStartPosition { get; set; }
        public long RecordListStartPosition { get; set; }
        public long RecordBlockInfoStartPosition { get; set; }
        public bool IsInit { get; set; }
        public List<MdictHelper.Tuple<long, long>> RecordBlockInfoList { get; set; }
        public List<MdictHelper.Idx> IdxBlockInfoList { get; set; }

        public MDict(string filePath)
        {
            Filename = filePath;

            IsInit = Init();
        }

        public bool Init()
        {
            try
            {
                var mdictStream = new StreamReader(Filename);

                //Get Headers
                var headerSizeBuffer = new byte[4];
                mdictStream.BaseStream.Read(headerSizeBuffer, 0, headerSizeBuffer.Length);
                var headerTextSize = int.Parse(BitConverter.ToString(headerSizeBuffer).Replace("-", ""), NumberStyles.HexNumber);

                var headerTextBuffer = new byte[headerTextSize];
                mdictStream.BaseStream.Read(headerTextBuffer, 0, headerTextBuffer.Length);
                var headerText = Encoding.UTF8.GetString(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, headerTextBuffer, 0, headerTextBuffer.Length - 2));

                var xmlResult = new XmlDocument();
                xmlResult.LoadXml(headerText);
                Headers = new Dictionary<string, string>();
                var xmlAttributeCollection = xmlResult.ChildNodes[0].Attributes;
                if (xmlAttributeCollection != null)
                    foreach (XmlAttribute at in xmlAttributeCollection)
                        Headers.Add(at.Name, at.Value);
                if (new[] { "GBK", "GB2312" }.Contains(Headers["Encoding"]))
                    Headers["Encoding"] = "GB18030";

                //Set NumberWidth
                if (float.Parse(Headers["GeneratedByEngineVersion"]) < 2.0)
                    NumberWidth = 4;
                else
                    NumberWidth = 8;

                //4 Unknown Bytes
                mdictStream.BaseStream.Read(new byte[4], 0, 4);

                //Set KeyListStartPosition
                KeyListStartPosition = mdictStream.BaseStream.Position;

                mdictStream.Close();

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool GetKeys()
        {
            try
            {
                var mdictStream = new StreamReader(Filename);
                mdictStream.BaseStream.Position = KeyListStartPosition;

                //Get KeyBlockInfoList
                var keyBlockInfoList = new List<MdictHelper.Tuple<long, long>>();

                int keyBlockInfoCount = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);
                int keyBlockInfoEntryCount = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);

                if (float.Parse(Headers["GeneratedByEngineVersion"]) >= 2.0)
                    MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);

                int keyBlockInfoSize = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);
                int keyBlockSize = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);

                if (float.Parse(Headers["GeneratedByEngineVersion"]) >= 2.0)
                    mdictStream.BaseStream.Read(new byte[4], 0, 4);

                var keyBlockInfoBuffer = new byte[keyBlockInfoSize];
                mdictStream.BaseStream.Read(keyBlockInfoBuffer, 0, keyBlockInfoBuffer.Length);

                try
                {
                    var keyBlockInfoText = BitConverter.ToString(keyBlockInfoBuffer);
                    if (float.Parse(Headers["GeneratedByEngineVersion"]) >= 2.0)
                        keyBlockInfoText =
                            MdictHelper.UnZipZlibHexString(keyBlockInfoText.Replace("-", "").Substring(NumberWidth*2))
                                .Replace("-", "");

                    int i = 0, byteWidth = 2, textTerm = 1;
                    if (float.Parse(Headers["GeneratedByEngineVersion"]) < 2.0)
                    {
                        byteWidth = 1;
                        textTerm = 0;
                    }

                    while (i < keyBlockInfoText.Length)
                    {
                        i += NumberWidth*2;

                        int textHeadSize = int.Parse(keyBlockInfoText.Substring(i, byteWidth*2),
                            NumberStyles.HexNumber);

                        i += byteWidth*2;

                        if (Headers["Encoding"] != "UTF-16")
                            i += (textHeadSize*2) + (textTerm*2);
                        else
                            i += ((textHeadSize*2) + (textTerm*2))*2;

                        int textTailSize = int.Parse(keyBlockInfoText.Substring(i, byteWidth*2),
                            NumberStyles.HexNumber);

                        i += byteWidth*2;

                        if (Headers["Encoding"] != "UTF-16")
                            i += (textTailSize*2) + (textTerm*2);
                        else
                            i += ((textTailSize*2) + (textTerm*2))*2;

                        int keyBlockCompressedSize = int.Parse(keyBlockInfoText.Substring(i, NumberWidth*2),
                            NumberStyles.HexNumber);
                        i += NumberWidth*2;
                        int keyBlockDecompressedSize = int.Parse(keyBlockInfoText.Substring(i, NumberWidth*2),
                            NumberStyles.HexNumber);

                        i += NumberWidth*2;

                        keyBlockInfoList.Add(new MdictHelper.Tuple<long, long>(keyBlockCompressedSize,
                            keyBlockDecompressedSize));
                    }
                }
                catch { }

                //Get KeyList
                var keyBlockBuffer = new byte[keyBlockSize];
                mdictStream.BaseStream.Read(keyBlockBuffer, 0, keyBlockBuffer.Length);

                string keyBlockText = BitConverter.ToString(keyBlockBuffer).Replace("-", "");

                //KeyBlockInfo not exist
                IdxBlockInfoList = new List<MdictHelper.Idx>();
                if (keyBlockInfoList.Count == 0)
                {
                    var keyBlockCompressed = new string[1];

                    switch (keyBlockText.Substring(0, 8))
                    {
                        case "00000000":

                            keyBlockCompressed = new[] { keyBlockText };

                            break;
                        case "01000000":
                            break;

                        case "02000000":

                            var stringSeparators = new[] { "02000000" };
                            keyBlockCompressed = keyBlockText.Split(stringSeparators, StringSplitOptions.RemoveEmptyEntries);

                            break;
                    }

                    int i = 0;
                    foreach (var kb in keyBlockCompressed)
                    {
                        string keyBlock = MdictHelper.UnZipZlibHexString(kb.Substring(8)).Replace("-", "");

                        var _kbs = MdictHelper.SplitUncompressesBlock(keyBlock, Headers["Encoding"]);
                        var idx = new MdictHelper.Idx() { Id = i++, FirstEntry = _kbs.Min(p => p.Value2), LastEntry = _kbs.Max(p => p.Value2) };
                        foreach (var _kb in _kbs)
                            idx.Entries.Add(new MdictHelper.Tuple<long, long, string>(_kb.Value1, -1, _kb.Value2));
                        IdxBlockInfoList.Add(idx);
                    }
                }
                else
                {
                    int l = 0, i = 0;
                    foreach (var kbi in keyBlockInfoList)
                    {
                        int st = l;
                        int ed = l + ((int)kbi.Value1 * 2);

                        switch (keyBlockText.Substring(0, 8))
                        {
                            case "00000000":
                                var _qf = MdictHelper.SplitUncompressesBlock(keyBlockText.Substring(st + (NumberWidth * 2), ed - (st + (NumberWidth * 2))), Headers["Encoding"]);
                                var idx = new MdictHelper.Idx() { Id = i++, FirstEntry = _qf.Min(p => p.Value2), LastEntry = _qf.Max(p => p.Value2) };
                                foreach (var kj in _qf)
                                    idx.Entries.Add(new MdictHelper.Tuple<long, long, string>(kj.Value1, -1, kj.Value2));
                                IdxBlockInfoList.Add(idx);
                                break;

                            case "01000000":
                                break;

                            case "02000000":
                                string keyBlock = MdictHelper.UnZipZlibHexString(keyBlockText.Substring(st + (NumberWidth * 2), ed - (st + (NumberWidth * 2)))).Replace("-", "");

                                var _qg = MdictHelper.SplitUncompressesBlock(keyBlock, Headers["Encoding"]);
                                var idx_ = new MdictHelper.Idx() { Id = i++, FirstEntry = _qg.Min(p => p.Value2), LastEntry = _qg.Max(p => p.Value2) };
                                foreach (var kj in _qg)
                                    idx_.Entries.Add(new MdictHelper.Tuple<long, long, string>(kj.Value1, -1, kj.Value2));
                                IdxBlockInfoList.Add(idx_);
                                break;
                        }

                        l += ((int)kbi.Value1 * 2);
                    }
                }

                long lastPosition = -1;
                for (int i = IdxBlockInfoList.Count - 1; i >= 0; i--)
                {
                    IdxBlockInfoList[i].Entries.Last().Value2 = lastPosition != -1 ? lastPosition - IdxBlockInfoList[i].Entries.Last().Value1 : -1;
                    for (int j = IdxBlockInfoList[i].Entries.Count - 1 - 1; j >= 0; j--)
                    {
                        IdxBlockInfoList[i].Entries[j].Value2 = IdxBlockInfoList[i].Entries[j + 1].Value1 - IdxBlockInfoList[i].Entries[j].Value1;
                    }
                    lastPosition = IdxBlockInfoList[i].Entries.First().Value1;
                }

                RecordBlockInfoStartPosition = mdictStream.BaseStream.Position;

                mdictStream.Close();

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool IgnoreKeys()
        {
            try
            {
                var mdictStream = new StreamReader(Filename);
                mdictStream.BaseStream.Position = KeyListStartPosition;

                int keyBlockInfoCount = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);
                int keyBlockInfoEntryCount = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);

                if (float.Parse(Headers["GeneratedByEngineVersion"]) >= 2.0)
                    MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);

                int keyBlockInfoSize = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);
                int keyBlockSize = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);

                if (float.Parse(Headers["GeneratedByEngineVersion"]) >= 2.0)
                    mdictStream.BaseStream.Read(new byte[4], 0, 4);

                mdictStream.BaseStream.Position += keyBlockInfoSize;

                mdictStream.BaseStream.Position += keyBlockSize;

                RecordBlockInfoStartPosition = mdictStream.BaseStream.Position;

                mdictStream.Close();

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool GetRecordBlocksInfo()
        {
            try
            {
                var mdictStream = new StreamReader(Filename);
                mdictStream.BaseStream.Position = RecordBlockInfoStartPosition;

                RecordBlockInfoList = new List<MdictHelper.Tuple<long, long>>();

                int numRecordBlocks = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);
                int numEntries = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);
                int recordBlockInfoSize = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);
                int recordBlockSize = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);

                for (int ml = 0; ml < numRecordBlocks; ml++)
                {
                    long compressedSize = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);
                    long decompressedSize = MdictHelper.ReadNumber(mdictStream.BaseStream, NumberWidth);

                    RecordBlockInfoList.Add(new MdictHelper.Tuple<long, long>(compressedSize, decompressedSize));
                }

                RecordListStartPosition = mdictStream.BaseStream.Position;

                mdictStream.Close();

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class Mdx : MDict
    {
        public Mdx(string filePath) : base(filePath) { }

        public Dictionary<int, string> StyleSheets { get; set; }

        public bool GetStyleSheets()
        {
            try
            {
                StyleSheets = new Dictionary<int, string>();
                string styleSheetsText = Headers.Single(p => p.Key == "StyleSheet").Value;
                string[] styleSheet = styleSheetsText.Split('\n');
                for (int i = 0; i < styleSheet.Length - 1; i += 3)
                    StyleSheets.Add(int.Parse(styleSheet[i]), styleSheet[i + 1]);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GetKeyValue(MdictHelper.Tuple<long, long, string> findKey)
        {
            try
            {
                var mdictStream = new StreamReader(Filename);
                mdictStream.BaseStream.Position = RecordListStartPosition;

                long h = 0, start = 0, ofset = 0;
                var findBlock = new MdictHelper.Tuple<long, long>(-1, -1);
                for (int kj = 0; kj < RecordBlockInfoList.Count; kj++)
                {
                    h += RecordBlockInfoList[kj].Value2;
                    ofset += RecordBlockInfoList[kj].Value1;

                    if (h > findKey.Value1)
                    {
                        findBlock = RecordBlockInfoList[kj];
                        start = RecordBlockInfoList[kj].Value2 - (h - findKey.Value1);

                        ofset -= RecordBlockInfoList[kj].Value1;

                        break;
                    }
                }

                mdictStream.BaseStream.Position += ofset;

                var findKeyBuffer = new byte[findBlock.Value1];
                mdictStream.BaseStream.Read(findKeyBuffer, 0, findKeyBuffer.Length);

                string findKeyCompressedText = BitConverter.ToString(findKeyBuffer).Replace("-", "");

                string findKeyDecompressedText = MdictHelper.UnZipZlibHexString(findKeyCompressedText.Substring(16));

                Encoding encoder = Headers["Encoding"] == "UTF-16" ? Encoding.Unicode : Encoding.UTF8;

                string findKeyText = encoder.GetString(MdictHelper.HexStringToByteArray(findKeyDecompressedText.Replace("-", "").Substring((int)start * 2, ((int)(findKey.Value2)) * 2))).Replace("\0", "");

                var pattern = new Regex(@"[`](?<id>\d*)[`]",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);
                MatchCollection _q = pattern.Matches(findKeyText);
                foreach (Match qm in _q)
                {
                    int id = int.Parse(qm.Groups["id"].Value);
                    findKeyText = findKeyText.Replace("`" + id + "`", StyleSheets.Single(p => p.Key == id).Value);
                }

                mdictStream.Close();

                if (!findKeyText.StartsWith("@@@LINK="))
                    return "<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01//EN\" \"http://www.w3.org/TR/html4/strict.dtd\" <html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" ><style type=\"text/css\">body{background-color:;color:;font-family:\"\";}</style></head><body>" + findKeyText + "</body></html>";
                return findKeyText;
            }
            catch
            {
                return null;
            }
        }
    }

    public class Mdd : MDict
    {
        public Mdd(string filePath) : base(filePath) { }

        public byte[] GetKeyValue(MdictHelper.Tuple<long, long, string> findKey)
        {
            try
            {
                var mdictStream = new StreamReader(Filename);
                mdictStream.BaseStream.Position = RecordListStartPosition;

                long h = 0, start = 0, ofset = 0;
                var findBlock = new MdictHelper.Tuple<long, long>(-1, -1);
                for (int kj = 0; kj < RecordBlockInfoList.Count; kj++)
                {
                    h += RecordBlockInfoList[kj].Value2;
                    ofset += RecordBlockInfoList[kj].Value1;

                    if (h > findKey.Value1)
                    {
                        findBlock = RecordBlockInfoList[kj];
                        start = RecordBlockInfoList[kj].Value2 - (h - findKey.Value1);

                        ofset -= RecordBlockInfoList[kj].Value1;

                        break;
                    }
                }

                mdictStream.BaseStream.Position += ofset;

                var findKeyBuffer = new byte[findBlock.Value1];
                mdictStream.BaseStream.Read(findKeyBuffer, 0, findKeyBuffer.Length);

                string findKeyCompressedText = BitConverter.ToString(findKeyBuffer).Replace("-", "");

                string findKeyDecompressedText = MdictHelper.UnZipZlibHexString(findKeyCompressedText.Substring(16));

                mdictStream.Close();

                return MdictHelper.HexStringToByteArray(findKeyDecompressedText.Replace("-", "").Substring((int)start * 2, (int)findKey.Value2 * 2));
            }
            catch
            {
                return null;
            }
        }
    }

    public class MdictHelper
    {
        public static byte[] HexStringToByteArray(string hexString)
        {
            if (hexString.Length % 2 != 0)
            {
                throw new ArgumentException(String.Format(CultureInfo.InvariantCulture, "The binary key cannot have an odd number of digits: {0}", hexString));
            }

            var hexAsBytes = new byte[hexString.Length / 2];
            for (int index = 0; index < hexAsBytes.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                hexAsBytes[index] = byte.Parse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return hexAsBytes;
        }

        public static string UnZipZlibHexString(string hexString)
        {
            byte[] gzBuffer = HexStringToByteArray(hexString);
            using (var ms = new MemoryStream())
            {
                ms.Write(gzBuffer, 0, gzBuffer.Length);

                ms.Position = 0;
                var gg = new List<byte>();
                using (var zip = new ZlibStream(ms, CompressionMode.Decompress))
                {
                    var buffer = new byte[1];
                    while (zip.Read(buffer, 0, buffer.Length) != 0)
                    {
                        gg.Add(buffer[0]);

                        buffer = new byte[1];
                    }
                }

                return BitConverter.ToString(gg.ToArray());
            }
        }

        public static int ReadNumber(Stream stream, int count)
        {
            var buffer = new byte[count];
            stream.Read(buffer, 0, buffer.Length);

            var numKeyBlocks = int.Parse(BitConverter.ToString(buffer).Replace("-", ""), NumberStyles.HexNumber);

            return numKeyBlocks;
        }

        public static List<Tuple<long, string>> SplitUncompressesBlock(string block, string encoding)
        {
            var result = new List<Tuple<long, string>>();
            const int numberWidth = 16;
            int keyStartIndex = 0;
            while (keyStartIndex < block.Length)
            {
                string keyId = block.Substring(keyStartIndex, numberWidth);

                string delimiter = "0000";
                int width = 4;
                var encoder = Encoding.Unicode;

                if (encoding == "UTF-8")
                {
                    delimiter = "00";
                    width = 2;
                    encoder = Encoding.UTF8;
                }

                int i = keyStartIndex + numberWidth;
                int keyEndIndex = 0;
                while (i < block.Length)
                {
                    if (block.Substring(i, width) == delimiter)
                    {
                        keyEndIndex = i;
                        break;
                    }
                    i += width;
                }

                string keyText = block.Substring(keyStartIndex + numberWidth, keyEndIndex - (keyStartIndex + numberWidth));
                keyText = encoder.GetString(HexStringToByteArray(keyText));

                keyStartIndex = keyEndIndex + width;

                var kid = long.Parse(keyId, NumberStyles.HexNumber);

                result.Add(new Tuple<long, string>(kid, keyText.Replace("\0", "")));
            }

            return result;
        }

        public class Tuple<T, K>
        {
            public Tuple()
            { }

            public T Value1;
            public K Value2;

            public Tuple(T v1, K v2)
            {
                Value1 = v1;
                Value2 = v2;
            }

            public override string ToString()
            {
                return Value1 + "Ⱨ" + Value2;
            }
        }

        public class Tuple<T, K, L>
        {
            public T Value1;
            public K Value2;
            public L Value3;

            public Tuple()
            { }

            public Tuple(T v1, K v2, L v3)
            {
                Value1 = v1;
                Value2 = v2;
                Value3 = v3;
            }

            public override string ToString()
            {
                return Value1 + "Ⱨ" + Value2 + "Ⱨ" + Value3;
            }
        }

        public class Idx
        {
            public Idx()
            {
                Entries = new List<Tuple<long, long, string>>();
            }

            public int Id { get; set; }

            public string FirstEntry { get; set; }

            public string LastEntry { get; set; }

            public List<Tuple<long, long, string>> Entries { get; set; }
        }
    }
}
