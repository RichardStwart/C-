﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shadowsocks.Encryption.Stream
{
    public class StreamRc4NativeEncryptor : StreamEncryptor
    {
        byte[] realkey = new byte[256];
        byte[] sbox = new byte[256];
        public StreamRc4NativeEncryptor(string method, string password) : base(method, password)
        {
        }

        protected override void initCipher(byte[] iv, bool isEncrypt)
        {
            base.initCipher(iv, isEncrypt);
            if (_cipher == CipherFamily.Rc4Md5)
            {
                byte[] temp = new byte[keyLen + ivLen];
                Array.Copy(_key, 0, temp, 0, keyLen);
                Array.Copy(iv, 0, temp, keyLen, ivLen);
                realkey = CryptoUtils.MD5(temp);
            }
            else
            {
                realkey = _key;
            }
            sbox = SBox(realkey);

        }

        protected override void cipherUpdate(bool isEncrypt, int length, byte[] buf, byte[] outbuf)
        {
            var ctx = isEncrypt ? enc_ctx : dec_ctx;

            byte[] t = new byte[length];
            Array.Copy(buf, t, length);

            RC4(ctx, sbox, t, length);
            Array.Copy(t, outbuf, length);
        }

        private static readonly Dictionary<string, CipherInfo> _ciphers = new Dictionary<string, CipherInfo>
        {
            // original RC4 doesn't use IV
            { "rc4", new CipherInfo("rc4", 16, 0, CipherFamily.Rc4) },
            { "rc4-md5", new CipherInfo("rc4-md5", 16, 16, CipherFamily.Rc4Md5) },
        };

        public static Dictionary<string, CipherInfo> SupportedCiphers()
        {
            return _ciphers;
        }

        protected override Dictionary<string, CipherInfo> getCiphers()
        {
            return _ciphers;
        }

        #region RC4
        class Context
        {
            public int index1 = 0;
            public int index2 = 0;
        }

        private Context enc_ctx = new Context();
        private Context dec_ctx = new Context();

        private byte[] SBox(byte[] key)
        {
            byte[] s = new byte[256];

            for (int i = 0; i < 256; i++)
            {
                s[i] = (byte)i;
            }

            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + key[i % key.Length] + s[i]) & 255;

                Swap(s, i, j);
            }

            return s;
        }

        private void RC4(Context ctx, byte[] s, byte[] data, int length)
        {
            for (int n = 0; n < length; n++)
            {
                byte b = data[n];

                ctx.index1 = (ctx.index1 + 1) & 255;
                ctx.index2 = (ctx.index2 + s[ctx.index1]) & 255;

                Swap(s, ctx.index1, ctx.index2);

                data[n] = (byte)(b ^ s[(s[ctx.index1] + s[ctx.index2]) & 255]);
            }
        }

        private static void Swap(byte[] s, int i, int j)
        {
            byte c = s[i];

            s[i] = s[j];
            s[j] = c;
        }
        #endregion
    }
}
