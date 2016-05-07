using System;
using System.Text;
using System.Security.Cryptography;

namespace pMd5
{
    public class Md5Calculator
    {
        public static byte[] CalculateMd5Bytes(byte[] input)
        {
            try
            {
                MD5 md5 = MD5.Create();
                byte[] hash = md5.ComputeHash(input);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                { sb.Append(hash[i].ToString("x2")); }
                return Encoding.UTF8.GetBytes(sb.ToString());
            }
            catch (Exception e) { throw e; }
        }

        public static string CalculateMd5String(byte[] input)
        {
            try
            {
                MD5 md5 = MD5.Create();
                byte[] hash = md5.ComputeHash(input);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                { sb.Append(hash[i].ToString("x2")); }
                return sb.ToString();
            }
            catch (Exception e) { throw e; }
        }

        public static byte[] CalculateMd5Bytes(string input)
        {
            try
            {
                MD5 md5 = MD5.Create();
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                { sb.Append(hash[i].ToString("x2")); }
                return Encoding.UTF8.GetBytes(sb.ToString());
            }
            catch (Exception e) { throw e; }
        }

        public static string CalculateMd5String(string input)
        {
            try
            {
                MD5 md5 = MD5.Create();
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                { sb.Append(hash[i].ToString("x2")); }
                return sb.ToString();
            }
            catch (Exception e) { throw e; }
        }
    }
}