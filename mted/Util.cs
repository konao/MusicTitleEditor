using System;
using System.Collections.Generic;
using System.Text;

namespace mted
{
    class Util
    {
        /// <summary>
        /// seq1のstartPosバイト目からをseq2と比較
        /// </summary>
        /// <param name="seq1">バイト配列1</param>
        /// <param name="startPos">比較開始地点</param>
        /// <param name="seq2">バイト配列2</param>
        /// <param name="compLen">比較するサイズ．指定なしの場合はseq2のサイズとなる．</param>
        /// <returns></returns>
        public static bool compareBytes(byte[] seq1, int startPos, byte[] seq2, int compLen = 0)
        {
            if (compLen == 0)
            {
                // 長さ指定なしならseq2の長さ分を比較
                compLen = seq2.Length;
            }

            for (int i = 0; i < compLen; i++)
            {
                if (seq1[startPos + i] != seq2[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// バイト配列からバイト配列へのコピー(0)
        ///  srcの先頭からバッファ長さ分すべてをdest[destStartPos]以降にコピー
        /// </summary>
        /// <param name="src">転送元バイト配列</param>
        /// <param name="dest">転送先バイト配列</param>
        /// <param name="destStartPos">転送先開始地点</param>
        public static void copyBytes0(byte[] src, ref byte[] dest, int destStartPos = 0)
        {
            for (int i = 0; i < src.Length; i++)
            {
                dest[destStartPos + i] = src[i];
            }
        }

        /// <summary>
        /// バイト配列からバイト配列へのコピー(1)
        /// srcの先頭からバッファ長さ分すべてをdest[destStartPos]以降にコピー
        /// 
        /// destStartPosは更新される
        /// </summary>
        /// <param name="src">転送元バイト配列</param>
        /// <param name="dest">転送先バイト配列</param>
        /// <param name="destStartPos">転送先開始地点．転送後に更新される</param>
        public static void copyBytes1(byte[] src, ref byte[] dest, ref int destStartPos)
        {
            for (int i = 0; i < src.Length; i++)
            {
                dest[destStartPos + i] = src[i];
            }

            destStartPos += src.Length;    // 位置更新（転送先）
        }


        /// <summary>
        /// バイト配列からバイト配列へのコピー(2)
        /// src[srcStartPos]からlenバイトをdest[destStartPos]以降にコピー
        /// 
        /// srcStartPosは更新される
        /// </summary>
        /// <param name="src">転送元バイト配列</param>
        /// <param name="srcStartPos">転送元開始地点．転送後に更新される</param>
        /// <param name="len">転送サイズ(単位：バイト)</param>
        /// <param name="dest">転送先バイト配列</param>
        /// <param name="destStartPos">転送先開始地点</param>
        public static void copyBytes2(byte[] src, ref int srcStartPos, int len, ref byte[] dest, int destStartPos = 0)
        {
            for (int i = 0; i < len; i++)
            {
                dest[destStartPos + i] = src[srcStartPos + i];
            }

            srcStartPos += len;    // 位置更新（転送元）
        }

        /// <summary>
        /// バイト配列からバイト配列へのコピー(3)
        /// src[srcStartPos]からlenバイトをdest[destStartPos]以降にコピー
        /// 
        /// srcStartPos, destStartPosの両方とも更新される
        /// </summary>
        /// <param name="src">転送元バイト配列</param>
        /// <param name="srcStartPos">転送元開始地点．転送後に更新される</param>
        /// <param name="len">転送サイズ(単位：バイト)</param>
        /// <param name="dest">転送先バイト配列</param>
        /// <param name="destStartPos">転送先開始地点．転送後に更新される</param>
        public static void copyBytes3(byte[] src, ref int srcStartPos, int len, ref byte[] dest, ref int destStartPos)
        {
            for (int i = 0; i < len; i++)
            {
                dest[destStartPos + i] = src[srcStartPos + i];
            }

            srcStartPos += len;    // 位置更新（転送元）
            destStartPos += len;    // 位置更新（転送先）
        }

        /// <summary>
        /// 文字コードを判別する
        /// </summary>
        /// <remarks>
        /// Jcode.pmのgetcodeメソッドを移植したものです。
        /// Jcode.pm(http://openlab.ring.gr.jp/Jcode/index-j.html)
        /// Jcode.pmの著作権情報
        /// Copyright 1999-2005 Dan Kogai <dankogai@dan.co.jp>
        /// This library is free software; you can redistribute it and/or modify it
        ///  under the same terms as Perl itself.
        /// </remarks>
        /// <param name="bytes">文字コードを調べるデータ</param>
        /// <returns>適当と思われるEncodingオブジェクト。
        /// 判断できなかった時はnull。</returns>
        public static System.Text.Encoding GetCode(byte[] bytes)
        {
            const byte bEscape = 0x1B;
            const byte bAt = 0x40;
            const byte bDollar = 0x24;
            const byte bAnd = 0x26;
            const byte bOpen = 0x28;    //'('
            const byte bB = 0x42;
            const byte bD = 0x44;
            const byte bJ = 0x4A;
            const byte bI = 0x49;

            int len = bytes.Length;
            byte b1, b2, b3, b4;

            //Encode::is_utf8 は無視

            bool isBinary = false;
            for (int i = 0; i < len; i++)
            {
                b1 = bytes[i];
                if (b1 <= 0x06 || b1 == 0x7F || b1 == 0xFF)
                {
                    //'binary'
                    isBinary = true;
                    if (b1 == 0x00 && i < len - 1 && bytes[i + 1] <= 0x7F)
                    {
                        //smells like raw unicode
                        return System.Text.Encoding.Unicode;
                    }
                }
            }
            if (isBinary)
            {
                return null;
            }

            //not Japanese
            bool notJapanese = true;
            for (int i = 0; i < len; i++)
            {
                b1 = bytes[i];
                if (b1 == bEscape || 0x80 <= b1)
                {
                    notJapanese = false;
                    break;
                }
            }
            if (notJapanese)
            {
                return System.Text.Encoding.ASCII;
            }

            for (int i = 0; i < len - 2; i++)
            {
                b1 = bytes[i];
                b2 = bytes[i + 1];
                b3 = bytes[i + 2];

                if (b1 == bEscape)
                {
                    if (b2 == bDollar && b3 == bAt)
                    {
                        //JIS_0208 1978
                        //JIS
                        return System.Text.Encoding.GetEncoding(50220);
                    }
                    else if (b2 == bDollar && b3 == bB)
                    {
                        //JIS_0208 1983
                        //JIS
                        return System.Text.Encoding.GetEncoding(50220);
                    }
                    else if (b2 == bOpen && (b3 == bB || b3 == bJ))
                    {
                        //JIS_ASC
                        //JIS
                        return System.Text.Encoding.GetEncoding(50220);
                    }
                    else if (b2 == bOpen && b3 == bI)
                    {
                        //JIS_KANA
                        //JIS
                        return System.Text.Encoding.GetEncoding(50220);
                    }
                    if (i < len - 3)
                    {
                        b4 = bytes[i + 3];
                        if (b2 == bDollar && b3 == bOpen && b4 == bD)
                        {
                            //JIS_0212
                            //JIS
                            return System.Text.Encoding.GetEncoding(50220);
                        }
                        if (i < len - 5 &&
                            b2 == bAnd && b3 == bAt && b4 == bEscape &&
                            bytes[i + 4] == bDollar && bytes[i + 5] == bB)
                        {
                            //JIS_0208 1990
                            //JIS
                            return System.Text.Encoding.GetEncoding(50220);
                        }
                    }
                }
            }

            //should be euc|sjis|utf8
            //use of (?:) by Hiroki Ohzaki <ohzaki@iod.ricoh.co.jp>
            int sjis = 0;
            int euc = 0;
            int utf8 = 0;
            for (int i = 0; i < len - 1; i++)
            {
                b1 = bytes[i];
                b2 = bytes[i + 1];
                if (((0x81 <= b1 && b1 <= 0x9F) || (0xE0 <= b1 && b1 <= 0xFC)) &&
                    ((0x40 <= b2 && b2 <= 0x7E) || (0x80 <= b2 && b2 <= 0xFC)))
                {
                    //SJIS_C
                    sjis += 2;
                    i++;
                }
            }
            for (int i = 0; i < len - 1; i++)
            {
                b1 = bytes[i];
                b2 = bytes[i + 1];
                if (((0xA1 <= b1 && b1 <= 0xFE) && (0xA1 <= b2 && b2 <= 0xFE)) ||
                    (b1 == 0x8E && (0xA1 <= b2 && b2 <= 0xDF)))
                {
                    //EUC_C
                    //EUC_KANA
                    euc += 2;
                    i++;
                }
                else if (i < len - 2)
                {
                    b3 = bytes[i + 2];
                    if (b1 == 0x8F && (0xA1 <= b2 && b2 <= 0xFE) &&
                        (0xA1 <= b3 && b3 <= 0xFE))
                    {
                        //EUC_0212
                        euc += 3;
                        i += 2;
                    }
                }
            }
            for (int i = 0; i < len - 1; i++)
            {
                b1 = bytes[i];
                b2 = bytes[i + 1];
                if ((0xC0 <= b1 && b1 <= 0xDF) && (0x80 <= b2 && b2 <= 0xBF))
                {
                    //UTF8
                    utf8 += 2;
                    i++;
                }
                else if (i < len - 2)
                {
                    b3 = bytes[i + 2];
                    if ((0xE0 <= b1 && b1 <= 0xEF) && (0x80 <= b2 && b2 <= 0xBF) &&
                        (0x80 <= b3 && b3 <= 0xBF))
                    {
                        //UTF8
                        utf8 += 3;
                        i += 2;
                    }
                }
            }
            //M. Takahashi's suggestion
            //utf8 += utf8 / 2;

            System.Diagnostics.Debug.WriteLine(
                string.Format("sjis = {0}, euc = {1}, utf8 = {2}", sjis, euc, utf8));
            if (euc > sjis && euc > utf8)
            {
                //EUC
                return System.Text.Encoding.GetEncoding(51932);
            }
            else if (sjis > euc && sjis > utf8)
            {
                //SJIS
                return System.Text.Encoding.GetEncoding(932);
            }
            else if (utf8 > euc && utf8 > sjis)
            {
                //UTF8
                return System.Text.Encoding.UTF8;
            }

            return null;
        }
    }
}
