using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwapClipboard
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }


    static class ExtensionForStream
    {
        public static string ReadLine(this Stream stream, char end = '\n')
        {
            List<byte> bytes = new List<byte>();
            while (stream.CanRead)
            {
                byte rd = (byte)stream.ReadByte();
                if (rd == end) break;
                bytes.Add(rd);
            }
            return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
        }

        public static void WriteLine(this Stream stream, string text = "")
        {
            WriteLine(stream, text, Encoding.UTF8);
        }

        public static void WriteLine(this Stream stream, string text, Encoding encoding)
        {
            byte[] bytes = encoding.GetBytes(text + "\r\n");
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
