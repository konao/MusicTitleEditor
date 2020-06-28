// ****************************************************************
//  MP3楽曲情報一括書き換えプログラム
//
//  mp3dirディレクトリの.mp3ファイルの曲情報を
//  トラック情報ファイルに従って書き換える
//
//  変換後のmp3ファイルはトラック情報ファイルのOUTDIRに設定したディレクトリに出力される
//
//  書式：
//  mted.exe [-s|-c] mp3dir [trackInfo.txt]
//
//  -s .............. mp3ファイル名変更(ファイル名先頭の番号を取り除く)
//  -c .............. トラック情報ファイルに従って変換
//  mp3dir .......... 入力mp3ファイルのあるディレクトリ
//  trackInfo.txt ... トラック情報ファイル
//
//  (ex)
//  > mted -s .     
//  カレントディレクトリのmp3ファイル名から先頭の番号を取り除く
//  （ファイル名先頭が1桁以上の数字とアンダーバーで始まっているもののみ対象）
//
//  > mted -c orig trackInfo.txt
//  orig/*.mp3のトラック情報をtrackInfo.txtに従って書き換える
//  更新後のmp3ファイルはtrackInfo.txtのOUTDIRに指定したディレクトリに出力される
//  (origディレクトリのmp3ファイルは変更されない）
//
//  ＜mp3フォーマットの詳細＞
// http://takaaki.info/wp-content/uploads/2013/01/ID3v2.3.0J.html#sec3
// http://www.cactussoft.co.jp/Sarbo/divMPeg3UnmanageFile.html
// 
//  2020/5/27 konao
// ****************************************************************

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace mted
{
    class Program
    {
        static void Main(string[] args)
        {
            // .NET Core 3.1ではshift_jisエンコーディングを使う前におまじないが必要
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding.GetEncoding("shift_jis");

            if (args.Length < 2)
            {
                Console.WriteLine("usage: mted.exe [-s|-c] mp3dir [trackInfo.txt]");
                return;
            }

            string cmd = args[0];
            string mp3Dir = "";
            string trackInfoFile = "";

            if (cmd == "-s")
            {
                mp3Dir = args[1];

                var reg = new Regex("[0-9]+_(?<core>.*)");
                IEnumerable<string> mp3Paths = Directory.EnumerateFiles(mp3Dir, "*.mp3");
                foreach (string mp3Path in mp3Paths)
                {
                    string mp3File = Path.GetFileName(mp3Path);
                    var mc = reg.Match(mp3File);
                    if (mc.Success)
                    {
                        // コピー前、後のパス名
                        string srcPath = Path.Combine(mp3Dir, mc.Value);
                        string destPath = Path.Combine(mp3Dir, mc.Groups["core"].Value);    // (?<core>.*)にマッチした部分を取り出す
                        // src --> dest
                        Console.WriteLine("{0} ---> {1}", srcPath, destPath);

                        // コピー先がすでにあれば消しておく
                        if (File.Exists(destPath))
                        {
                            File.Delete(destPath);
                        }

                        // コピー
                        File.Copy(srcPath, destPath);
                    }
                }
            }
            else if (cmd == "-c")
            {
                mp3Dir = args[1];
                trackInfoFile = args[2];

                // トラック情報ファイル読み込み
                TrackInfo trinfo = new TrackInfo();
                if (!trinfo.Read(trackInfoFile))
                {
                    Console.WriteLine("Failed to read ({0})", trackInfoFile);
                    return;
                }

                trinfo.Print();

                var mp3 = new MP3Data();

                // トラック情報ファイル出現順にループ
                string[] mp3Files = trinfo.GetMp3Files();
                foreach (string mp3File in mp3Files)
                {
                    string mp3Path = Path.Combine(mp3Dir, mp3File);

                    // 曲情報更新
                    if (!mp3.UpdateTrackInfo(mp3Path, trinfo))
                    {
                        // エラー
                        Console.WriteLine("[Error] failed to convert MP3 file. ({0})", mp3File);
                        return;
                    }

                    // 成功
                    Console.WriteLine("{0} ... OK", mp3File);
                }
            }
            else
            {
                Console.WriteLine("unknown cmd");
                return;
            }
        }
    }

    /// <summary>
    /// mp3ヘッダ中のフレームを書き換える値
    /// </summary>
    class FrameInfo
    {
        // key=フレーム名
        // (ex) "TPE1" # 参加アーティスト名
        //
        // value=書き換える値
        // (ex) "μ's"
        Dictionary<string, string> _info = new Dictionary<string, string>();

        public string this [string key] {
            get { return _info[key]; }
            set { _info[key] = value; }
        }

        public Dictionary<string, string> info
        {
            get { return _info; }
        }
    }

    /// <summary>
    /// 書き換え情報ファイル
    /// </summary>
    class TrackInfo
    {
        // 曲名によらない共通の設定値（アルバム名、ジャンル）
        //
        // キー: フレームID（4文字の文字列）
        // 値：文字列
        FrameInfo _common = new FrameInfo();

        // 曲名ごとに異なる設定値（例：タイトル）
        // キー：ファイル名
        // 値：TrackElemsオブジェクト
        Dictionary<string, FrameInfo> _tracks = new Dictionary<string, FrameInfo>();

        /// <summary>
        /// 出力ディレクトリ(トラック情報ファイルのOUTDIR設定値)
        /// </summary>
        string _outdir;
        public string OutDir
        {
            get { return _outdir; }
        }

        /// <summary>
        /// mp3ファイルが何番目のトラックかを返す
        /// 
        /// トラック番号はトラック情報ファイルの記述順で決まる
        /// </summary>
        /// <param name="inMp3File">mp3ファイル名</param>
        /// <returns>トラック番号．mp3ファイル名がトラック情報ファイルに存在しなかった場合は-1が返る</returns>
        public int GetTrackNo(string inMp3File)
        {
            try
            {
                return int.Parse(_tracks[inMp3File]["TRCK"]);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// トラック番号順で入力MP3ファイル名のリストを返す
        /// </summary>
        /// <returns></returns>
        public string[] GetMp3Files()
        {
            return _tracks
                .OrderBy(x => int.Parse(x.Value["TRCK"]))
                .Select(x => x.Key)
                .ToArray();
        }

        enum MODE
        {
            NONE, SYSTEM, COMMON, TRACKS
        };

        /// <summary>
        /// トラック情報ファイルを読み込む
        /// </summary>
        /// <param name="trackInfoFile">トラック情報ファイル</param>
        /// <returns>true=成功、false=失敗</returns>
        public bool Read(string trackInfoFile)
        {
            if (!File.Exists(trackInfoFile))
            {
                return false;
            }

            string[] trackInfoLines = File.ReadAllLines(trackInfoFile, Encoding.GetEncoding("shift_jis"));
            MODE mode = MODE.NONE;

            int trackNo = 1;
            foreach (string line in trackInfoLines)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    // 空行またはコメントならスキップ
                    continue;
                }

                //Console.WriteLine(">>> {0}", line);

                if (line == "[SYSTEM]")
                {
                    mode = MODE.SYSTEM;
                }
                else if (line == "[COMMON]")
                {
                    mode = MODE.COMMON;
                }
                else if (line == "[TRACKS]")
                {
                    mode = MODE.TRACKS;
                }
                else
                {
                    if (mode == MODE.SYSTEM)
                    {
                        // [SYSTEM]モードでの処理
                        string[] s = line.Split("=");
                        if (s.Length == 2)
                        {
                            string key = s[0].Trim();
                            string value = s[1].Trim();

                            if (key == "OUTDIR")
                            {
                                // 出力jディレクトリ
                                _outdir = value;
                            }
                        }
                    }
                    else if (mode == MODE.COMMON)
                    {
                        // [COMMON]モードでの処理
                        // key=valueで分解してマップに登録
                        string[] s = line.Split("=");
                        if (s.Length == 2)
                        {
                            string key = s[0].Trim();
                            string value = s[1].Trim();

                            _common[key] = value;
                        }
                    }
                    else if (mode == MODE.TRACKS)
                    {
                        // [TRACKS]モードでの処理
                        // lineをコロン(:)で分解して情報をマップに登録
                        string[] elems = line.Split(":");

                        if (elems.Length >= 1)
                        {
                            string mp3FileName = elems[0].Trim();   // mp3ファイル名

                            FrameInfo fi = new FrameInfo();
                            fi["TRCK"] = trackNo.ToString();    // トラック番号(トラック情報ファイルの出現順）

                            if (elems.Length >= 2)
                            {
                                // タイトル指定あり
                                fi["TIT2"] = elems[1].Trim();   // タイトル
                            }
                            else
                            {
                                // タイトル指定がない場合
                                // ファイル名のベース部分をそのままタイトルに使う
                                fi["TIT2"] = Path.GetFileNameWithoutExtension(mp3FileName);
                            }

                            _tracks[mp3FileName] = fi;

                            trackNo++;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// フレームIDに対応するバイナリデータを生成する
        /// </summary>
        /// <param name="mp3FileName">曲ファイル名</param>
        /// <param name="frameID">フレームID(4文字のID)</param>
        /// <param name="binData">変換後のバイナリデータ</param>
        /// </param>
        /// <returns></returns>
        /// <remarks>
        /// バイナリデータのフォーマット：
        /// 
        /// ・フレームID (4bytes)
        /// ・フレームサイズ (4bytes - BigEndian) --> N
        /// ・フラグ (2bytes)
        /// ・データ (N bytes)
        ///     EncodeFlag (1byte) 00=ASCII, 01=Unicode
        ///     if EncodeFlag==00
        ///         データ本体(ASCII) (N-1 bytes)
        ///     else
        ///         0xFF, 0xFE (Little Endian Mark)
        ///         データ本体(Unicode - LittleEndian) (N-3 bytes)
        ///     end
        /// </remarks>
        public bool GetEncodedBytes(string mp3FileName, string frameID, out string convertedValue, out byte[] binData)
        {
            convertedValue = "";
            binData = null;
            string value = "";

            // トラック情報ファイルの内容からframeIDに対応する値を得る
            try
            {
                // frameIDは共通設定にあるか
                value = _common[frameID];
            }
            catch
            {
                // frameIDはファイル別設定にあるか
                try
                {
                    FrameInfo fi = _tracks[mp3FileName];
                    value = fi[frameID];
                }
                catch
                {
                    // frameIDに対応するデータはない
                    return false;
                }
            }

            // 変換後の値を呼び出し元に返す
            convertedValue = value;

            Encoding sjis = Encoding.GetEncoding("sjis");
            Encoding uniLE = Encoding.Unicode;

            byte[] bnFrameID = Encoding.ASCII.GetBytes(frameID);
            byte[] bnFlag = new byte[2] { 0x00, 0x00 };

            // valueをバイナリデータ化する --> bnValue
            byte[] bnValue = null;
            byte[] bnTmp1 = sjis.GetBytes(value);   // valueをsjisバイト列に変換
            if (bnTmp1.Length == value.Length)
            {
                // 文字数 == バイト数ならASCIIだろう
                bnValue = new byte[value.Length + 1];
                bnValue[0] = 0x00;  // ASCII
                Util.copyBytes0(bnTmp1, ref bnValue, 1);
            }
            else
            {
                // そうでなければ日本語(SJIS)

                // SJIS --> Unicode(LE)
                byte[] sjisValue = sjis.GetBytes(value);
                byte[] uniValue = Encoding.Convert(sjis, uniLE, sjisValue, 0, sjisValue.Length);

                bnValue = new byte[uniValue.Length + 3];
                bnValue[0] = 0x01;  // unicode
                bnValue[1] = 0xFF;  // unicode BOM (LittleEndian)
                bnValue[2] = 0xFE;  // unicode BOM (LittleEndian)
                Util.copyBytes0(uniValue, ref bnValue, 3);
            }

            // フレームの長さ
            // bnValue.LengthをbigEndianで4バイトデータにする
            byte[] bnLength = BitConverter.GetBytes(bnValue.Length);

            // littleEndian --> bigEndian
            byte tmp;
            tmp = bnLength[0];
            bnLength[0] = bnLength[3];
            bnLength[3] = tmp;
            tmp = bnLength[1];
            bnLength[1] = bnLength[2];
            bnLength[2] = tmp;

            int totalLength = bnFrameID.Length + bnLength.Length + bnFlag.Length + bnValue.Length;
            binData = new byte[totalLength];

            int destStartPos = 0;
            Util.copyBytes1(bnFrameID, ref binData, ref destStartPos);
            Util.copyBytes1(bnLength, ref binData, ref destStartPos);
            Util.copyBytes1(bnFlag, ref binData, ref destStartPos);
            Util.copyBytes1(bnValue, ref binData, ref destStartPos);

            return true;
        }

        public void Print()
        {
            Console.WriteLine("**** Common ****");
            foreach (var (k, v) in _common.info)
            {
                Console.WriteLine("{0} --> {1}", k, v);
            }

            Console.WriteLine("**** Tracks ****");
            foreach (var (k, o) in _tracks)
            {
                Console.WriteLine("{0}", k);
                Console.WriteLine("\tTrackNo: {0}", o["TRCK"]);
                Console.WriteLine("\tTitle:   {0}", o["TIT2"]);
            }
        }
    }

    class MP3Data
    {
        byte[] _inData = null;  // 入力mp3ファイルのデータ
        byte[] _outData = null; // 出力（更新後）のmp3データ

        /// <summary>
        /// mp3ファイルの曲情報をトラック情報ファイルに従って書き換え、別のmp3ファイルとして出力する
        /// </summary>
        /// <param name="inMp3Path">入力mp3ファイル</param>
        /// <param name="trInfo">トラック情報．出力先ディレクトリはこの中にある</param>
        /// <returns>true=成功, false=失敗</returns>
        public bool UpdateTrackInfo(string inMp3Path, TrackInfo trInfo)
        {
            string inMp3File = Path.GetFileName(inMp3Path);

            if (!File.Exists(inMp3Path))
            {
                return false;
            }

            string outdir = trInfo.OutDir;
            if (!Directory.Exists(outdir))
            {
                try
                {
                    Directory.CreateDirectory(outdir);
                }
                catch
                {
                    // 出力ディレクトリが作成できなかった
                    Console.WriteLine("Faild to create output directory ({0})", outdir);
                    return false;
                }
            }

            int trackNo = trInfo.GetTrackNo(inMp3File);
            if (trackNo < 0)
            {
                return false;
            }

            // 入力ファイルデータを読み込む
            if (!ReadAll(inMp3Path, out _inData))
            {
                Console.WriteLine("load error");
                return false;
            }

            // mp3ヘッダ情報取得
            int ID3v2HeaderSize;
            if (!GetID3v2HeaderInfo(_inData, out ID3v2HeaderSize))
            {
                Console.WriteLine("not MP3 format");
                return false;
            }

            //Console.WriteLine("ID3v2 HeaderSize = 0x{0}", ID3v2HeaderSize.ToString("x4"));

            // 出力バッファ確保
            _outData = new byte[_inData.Length];

            // mp3ヘッダ部分をコピー
            int inCurPos = 0;
            int outCurPos = 0;
            Util.copyBytes3(_inData, ref inCurPos, 10, ref _outData, ref outCurPos);

            int inNextPos;
            string frameID;
            while (GetNextFrame(_inData, inCurPos, out inNextPos, out frameID)) {

                string convertedValue;
                byte[] xferBinData;
                if (trInfo.GetEncodedBytes(inMp3File, frameID, out convertedValue, out xferBinData))
                {
#if DEBUG_LOG
                    Console.WriteLine("{0} ---> {1}", frameID, convertedValue);
#endif
                    // frameIDに対応する変換データがあった．
                    // 変換後のバイナリデータはxferBinDataに入っている
                    Util.copyBytes1(xferBinData, ref _outData, ref outCurPos);
                }
                else
                {
#if DEBUG_LOG
                    Console.WriteLine("{0}", frameID);
#endif
                    // frameIDに対応する変換データはなかった．
                    // _inDataをそのまま出力バッファにコピー
                    int len = inNextPos - inCurPos;
                    Util.copyBytes3(_inData, ref inCurPos, len, ref _outData, ref outCurPos);
                }

                inCurPos = inNextPos;
            }

            // ヘッダは10バイトあるので、ヘッダサイズ+10がデータ部分の先頭アドレスになる．
            int topAddrOfRestData = ID3v2HeaderSize + 10;

            // 現在のoutCurPosからtopAddrOfRestDataまでをゼロクリア
            for (int i=outCurPos; i<topAddrOfRestData; i++)
            {
                _outData[i] = 0x00;
            }

            // topAddrOfRestDataからファイルの最後までを出力バッファにコピー
            int restSize = _inData.Length - topAddrOfRestData;
            Util.copyBytes2(_inData, ref topAddrOfRestData, restSize, ref _outData, topAddrOfRestData);

            // 出力先ファイルに書き込む
            string outMp3Path = Path.Combine(outdir, string.Format("{0:D2}_{1}", trackNo, inMp3File));
            if (!WriteAll(outMp3Path, _outData))
            {
                Console.WriteLine("Write Error: {0}", outMp3Path);
                return false;
            }

            return true;
        }

        /// <summary>
        /// mp3ファイルを読み込みバイト列として返す
        /// </summary>
        /// <param name="fpath">入力mp3ファイルパス</param>
        /// <param name="data">読み込んだバイト列</param>
        /// <returns>true=成功, false=失敗</returns>
        public bool ReadAll(string fpath, out byte[] data)
        {
            data = null;

            if (!File.Exists(fpath))
            {
                return false;
            }

            FileInfo fi = new FileInfo(fpath);
            int fileSize = (int)fi.Length;

            using (BinaryReader reader = new BinaryReader(File.Open(fpath, FileMode.Open)))
            {
                data = new byte[fileSize];
                try
                {
                    int actualReadLen = reader.Read(data, 0, fileSize);

                    if (actualReadLen != fileSize)
                    {
                        // サイズが合ってない．読み込みエラー
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// バイト列をmp3ファイルとして出力
        /// </summary>
        /// <param name="fpath">出力先mp3ファイルパス</param>
        /// <param name="data">書き込むバイト列</param>
        /// <returns>true=成功, false=失敗</returns>
        public bool WriteAll(string fpath, byte[] data)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(fpath, FileMode.OpenOrCreate)))
            {
                try
                {
                    writer.Write(data);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// mp3ファイルのヘッダ情報を得る
        /// </summary>
        /// <param name="data">mp3データ</param>
        /// <param name="ID3v2HeaderSize">ヘッダ部分のサイズ</param>
        /// <returns>true=dataはmp3ファイルのヘッダだった, false=mp3ファイルではなかった</returns>
        /// <remarks>
        /// mp3のID3v2ヘッダは、先頭10バイトが固定で、その後可変部分が続く．
        /// ID3v2HeaderSizeに返ってくるのはこの可変部分．
        /// すなわちID3v2HeaderSize+10バイト目からが音楽データ本体である．
        /// </remarks>
        private bool GetID3v2HeaderInfo(byte[] data, out int ID3v2HeaderSize)
        {
            ID3v2HeaderSize = 0;

            if (!Util.compareBytes(data, 0, System.Text.Encoding.UTF8.GetBytes("ID3")))
            {
                // ID3v2フォーマットではない
                return false;
            }

            byte[] ver = new byte[2] { 0x03, 0x00 };   // ID3v2バージョン(2byte)
            if (!Util.compareBytes(data, 3, ver))
            {
                // format error
                return false;
            }

            // ID3v2フォーマットのデータサイズは、先頭から7バイト目
            // (インデックスでは6)からの4バイトに格納されているが、
            // フォーマットが少々特殊．
            //
            // (1) bigEndian
            // (2) 各バイトが7bitのデータとして格納されている（合計28bit)
            //
            // ビットパターンで表すと以下の通り．
            // 0aaa aaaa 0bbb bbbb 0ccc cccc 0ddd dddd
            //
            // 従って、データサイズを普通の変数で表現するには、
            // ビットパターンの変換が必要．
            //
            int sizeTop = 6;    // サイズ開始位置
            ID3v2HeaderSize = data[sizeTop+3]; // 0ddd dddd部分
            ID3v2HeaderSize += (data[sizeTop + 2] * 0x80);    // 0ccc cccc部分
            ID3v2HeaderSize += (data[sizeTop + 1] * 0x4000);  // 0bbb bbbb部分
            ID3v2HeaderSize += (data[sizeTop + 0] * 0x200000);    // 0aaa aaaa部分

            return true;
        }

        /// <summary>
        /// mp3ヘッダに含まれるフレーム情報を取り出す
        /// </summary>
        /// <param name="data">mp3データ</param>
        /// <param name="startPos">解析開始位置(>=0)</param>
        /// <param name="nextPos">次のフレーム開始位置</param>
        /// <param name="frameID">取り出したフレーム名("TRCK", "TIT2", etc)</param>
        /// <returns>true=フレーム取り出し成功, false=もう次のフレームはない</returns>
        private bool GetNextFrame(byte[] data, int startPos, out int nextPos, out string frameID)
        {
            // デフォルト値設定
            nextPos = startPos;
            frameID = "";

            int curPos = startPos;

            // フレームID(4バイトの文字列)を読み込む
            byte[] chunkHeader = new byte[4];
            Util.copyBytes2(data, ref curPos, 4, ref chunkHeader);

            if (chunkHeader[0] == 0x00)
            {
                // ID3v2ヘッダ終了
                return false;
            }

            frameID = System.Text.Encoding.UTF8.GetString(chunkHeader);
            //Console.WriteLine("---------------------------");
            //Console.WriteLine(frameID);

            // フレームサイズを読み込む
            // (4バイト、BigEndianで格納）
            byte[] chunkLength = new byte[4];
            Util.copyBytes2(data, ref curPos, 4, ref chunkLength);

            // bigEndian --> littleEndian
            byte tmp;
            tmp = chunkLength[0];
            chunkLength[0] = chunkLength[3];
            chunkLength[3] = tmp;
            tmp = chunkLength[1];
            chunkLength[1] = chunkLength[2];
            chunkLength[2] = tmp;

            int length = BitConverter.ToInt32(chunkLength, 0);

            // フラグ(2バイト)を読み込む
            byte[] chunkDummy = new byte[2];
            Util.copyBytes2(data, ref curPos, 2, ref chunkDummy);

            // ASCII/Unicode判定バイト(1バイト)を読み込む
            // 0=ASCII
            // 1=Unicode
            byte[] byASCIIorUnicode = new byte[1];
            Util.copyBytes2(data, ref curPos, 1, ref byASCIIorUnicode);
            //Console.WriteLine("byASCIIorUnicode={}", byASCIIorUnicode[0].ToString());

            int dataType = 0;   // 0=ASCII, 1=Unicode
            int dataLen;
            bool specialCase = false;
            if (byASCIIorUnicode[0] == 0)
            {
                // ASCII
                dataType = 0;
                dataLen = length - 1;
            }
            else if (byASCIIorUnicode[0] == 1)
            {
                // Unicode
                dataType = 1;
                dataLen = length - 1;
            }
            else
            {
                // イレギュラーなフォーマットタイプ
                // 先頭バイトが0でも1でもないときは、ASCIIデータ中の先頭1バイト
                specialCase = true;
                dataLen = length;
            }

            // データ本体部分を読む
            string dataStr;
            if (dataType == 0)
            {
                // データ本体を読む
                byte[] contents = new byte[dataLen];
                if (specialCase)
                {
                    // イレギュラーなフォーマットの場合

                    // 先頭1バイトはすでに読んでいる
                    contents[0] = byASCIIorUnicode[0];
                    // 残りのデータを読む
                    Util.copyBytes2(data, ref curPos, dataLen-1, ref contents, 1);
                }
                else
                {
                    // 通常のフォーマットの場合
                    Util.copyBytes2(data, ref curPos, dataLen, ref contents);
                }

                dataStr = System.Text.Encoding.UTF8.GetString(contents);
            }
            else
            {
                // バイトオーダー指定(2バイト)を読み込む
                // 0xFF 0xFE = Little Endian
                // 0xFE 0xFF = Big Endian
                byte[] ord = new byte[2];
                Util.copyBytes2(data, ref curPos, 2, ref ord);

                // 残りのデータ部分を読む
                byte[] contents = new byte[dataLen - 2];
                Util.copyBytes2(data, ref curPos, dataLen - 2, ref contents);

                if ((ord[0] == 0xFF) && (ord[1] == 0xFE))
                {
                    dataStr = System.Text.Encoding.Unicode.GetString(contents);
                }
                else if ((ord[0] == 0xFE) && (ord[1] == 0xFF))
                {
                    dataStr = System.Text.Encoding.BigEndianUnicode.GetString(contents);
                }
                else
                {
                    // フォーマットエラー
                    Console.WriteLine("wrong format");
                    return false;
                }
            }

            nextPos = curPos;

            return true;
        }
    }
}
