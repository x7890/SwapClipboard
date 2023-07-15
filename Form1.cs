using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Specialized;
using System.Web;

namespace SwapClipboard
{
    public partial class Form1 : Form
    {
        TcpListener tcpListener = null;
        Icon greenIcon, redIcon;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // 自动选中可用的IP
            cbbIp_DropDown();
            if (cbbIp.Items.Count == 0)
            {
                MessageBox.Show("请检查网卡设置", "无法获取局域网IP地址");
                Application.Exit();
            }

            // 提取文件名中设置的端口号
            string exeName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName);
            string exeSetting = Path.GetExtension(exeName);
            if (exeSetting != "")
            {
                try
                {
                    numPort.Value = Convert.ToInt32(exeSetting.Substring(1));
                }
                catch (FormatException)
                {
                }
            }

            // 自动启动服务
            btnStart_Click();

            // 获取绿色图标，生成红色图标
            greenIcon = notifyIcon.Icon;
            Bitmap bitmap = greenIcon.ToBitmap();
            for (int i = 0; i < bitmap.Height; i++)
            {
                for (int j = 0; j < bitmap.Width; j++)
                {
                    Color color = bitmap.GetPixel(i, j);
                    bitmap.SetPixel(i, j, Color.FromArgb(color.A, color.G, color.R, color.B));
                }
            }
            redIcon = Icon.FromHandle(bitmap.GetHicon());
        }
        private void Form1_Shown(object sender, EventArgs e)
        {
            if (Environment.GetCommandLineArgs().Length > 1)
            {
                Hide(); // 初次显示，有命令行参数，则隐藏窗口
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            notifyIcon.Visible = false; // 隐藏托盘图标，防止鬼影
        }

        private void cbbIp_DropDown(object sender = null, EventArgs e = null)
        {
            // 添加可用的IP
            int lastSel = cbbIp.SelectedIndex;
            cbbIp.Items.Clear();
            foreach (IPAddress iPAddress in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (iPAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    string ip = iPAddress.ToString();
                    cbbIp.Items.Add(ip);
                    if (sender == null)
                    {
                        if (cbbIp.Items.Count == 1)
                        {
                            cbbIp.SelectedIndex = 0;
                        }
                        else if (cbbIp.SelectedIndex == 0 && ip.StartsWith("192.168."))
                        {
                            cbbIp.SelectedIndex = cbbIp.Items.Count - 1;
                        }
                    }
                }
            }
            if (sender != null)
            {
                if (lastSel >= cbbIp.Items.Count)
                {
                    cbbIp.SelectedIndex = 0;
                }
                else
                {
                    cbbIp.SelectedIndex = lastSel;
                }
            }
        }

        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // 双击托盘图标时，显示窗口
            Show();
            Activate();
        }

        private void menuQuit_Click(object sender, EventArgs e)
        {
            // 清理所有内容，退出程序
            if (!numPort.Enabled) btnStart_Click(); // 服务未关闭，需要停止服务
            Application.Exit();
        }

        private void btnHide_Click(object sender, EventArgs e)
        {
            // 隐藏窗口
            Hide();
        }


        private void btnStart_Click(object sender = null, EventArgs e = null)
        {
            if (btnStart.Text == "启动")
            {
                try
                {
                    tcpListener = new TcpListener(new IPEndPoint(IPAddress.Parse(cbbIp.SelectedItem.ToString()), (int)numPort.Value));
                    tcpListener.Start();
                    tcpListener.BeginAcceptTcpClient(tcpCallback, null);
                }
                catch (Exception err)
                {
                    MessageBox.Show(err.Message, "无法启动服务");
                    return;
                }
                cbbIp.Enabled = numPort.Enabled = false;
                btnStart.Text = "停止";
            }
            else
            {
                tcpListener.Stop();
                cbbIp.Enabled = numPort.Enabled = true;
                btnStart.Text = "启动";
            }
        }


        enum ClipboardActions
        {
            SendToPc = 1,
            TypeToPc = 2,
            GetPc = 3,
            Swap = 4,
        }


        private async void tcpCallback(IAsyncResult ar)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = tcpListener.EndAcceptTcpClient(ar);
            }
            catch (ObjectDisposedException)
            {
                tcpListener = null;
                return; // 已通过Stop()方法停止
            }
            tcpListener.BeginAcceptSocket(tcpCallback, null); // 准备下一次
            NetworkStream stream = tcpClient.GetStream();
            notifyIcon.Icon = redIcon;

            try
            {
                // 读取POST报文头
                string line = stream.ReadLine();
                ClipboardActions action = ClipboardActions.Swap;
                string contentType = "", fileExt = "";
                long contentLen = 0;
                while ((line = stream.ReadLine()) != "")
                {
                    Debug.WriteLine(line);
                    if (line.StartsWith("Content-Type: ")) contentType = line.Substring("Content-Type: ".Length);
                    if (line.StartsWith("Content-Length: ")) contentLen = Convert.ToInt64(line.Substring("Content-Length: ".Length));
                    if (line.StartsWith("File-Ext: ")) fileExt = line.Substring("File-Ext: ".Length);
                    if (line.StartsWith("Action: ")) action = (ClipboardActions)Convert.ToInt16(line.Substring("Action: ".Length, 1));
                }
                if (action == ClipboardActions.TypeToPc) action = ClipboardActions.SendToPc;

                string sendType = "text/plain", sendFile = "";
                byte[] sendBuf = new byte[0] { }; // 先获取剪贴板，以免被覆盖
                if (action == ClipboardActions.GetPc || action == ClipboardActions.Swap) // 需要发送PC剪贴板
                {
                    if ((bool)Invoke(new Func<bool>(() => Clipboard.ContainsImage())))
                    {
                        Image image = (Image)Invoke(new Func<Image>(() => Clipboard.GetImage()));
                        sendType = "image/png";
                        MemoryStream memImage = new MemoryStream();
                        image.Save(memImage, System.Drawing.Imaging.ImageFormat.Png);
                        sendBuf = memImage.ToArray();
                    }
                    else if ((bool)Invoke(new Func<bool>(() => Clipboard.ContainsFileDropList())))
                    {
                        StringCollection files = ((StringCollection)Invoke(new Func<StringCollection>(() => Clipboard.GetFileDropList())));
                        sendFile = files[0];
                        sendType = MimeMapping.GetMimeMapping(sendFile);
                    }
                    else
                    {
                        sendBuf = Encoding.UTF8.GetBytes((string)Invoke(new Func<string>(() => Clipboard.GetText())));
                    }
                }

                byte[] recvBuf;
                // 按文件类型送入剪贴板
                if ((action == ClipboardActions.SendToPc || action == ClipboardActions.Swap) && fileExt != "") // 需要接收剪贴板
                {
                    notifyIcon.Text = "接收中";
                    if (contentType.StartsWith("image/") || contentType == "text/plain")
                    {
                        recvBuf = new byte[contentLen];
                        while (contentLen > 0)
                        {
                            int readLen = await stream.ReadAsync(recvBuf, recvBuf.Length - (int)contentLen, (int)contentLen);
                            contentLen -= readLen;
                            notifyIcon.Text = "还需要接收：" + (contentLen / 1024 / 1024).ToString() + " MB";
                        }
                        if (contentType.StartsWith("image/"))
                        {
                            Image img = Image.FromStream(new MemoryStream(recvBuf, 0, recvBuf.Length));
                            Invoke(new Action(() => Clipboard.SetImage(img)));
                        }
                        else if (contentType == "text/plain")
                        {
                            string copy = Encoding.UTF8.GetString(recvBuf);
                            Invoke(new Action(() => Clipboard.SetText(copy)));
                        }
                    }
                    else
                    {
                        string tmpPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName().Replace(".", "") + "." + fileExt);

                        FileStream fstream = File.Create(tmpPath);
                        recvBuf = new byte[4096];
                        while (contentLen > 0)
                        {
                            int readLen = await stream.ReadAsync(recvBuf, 0, (int)Math.Min(recvBuf.Length, contentLen));
                            fstream.Write(recvBuf, 0, readLen);
                            contentLen -= readLen;
                            notifyIcon.Text = "还需要接收：" + (contentLen / 1024 / 1024).ToString() + " MB";
                        }
                        fstream.Close();
                        notifyIcon.Text = Text;

                        // 移动文件
                        DataObject data = new DataObject();
                        var files = new System.Collections.Specialized.StringCollection { tmpPath };
                        data.SetFileDropList(files);
                        MemoryStream dropEffect = new MemoryStream();
                        dropEffect.Write(new byte[] { 2, 0, 0, 0 }, 0, 4);
                        data.SetData("Preferred DropEffect", dropEffect);
                        Invoke(new Action(() => Clipboard.SetDataObject(data, true)));
                    }
                }

                // 收完再发
                notifyIcon.Text = "发送中";
                stream.WriteLine("HTTP/1.1 200 OK");
                stream.WriteLine("Content-Type: " + sendType);
                if (sendFile == "") // 需要发送PC剪贴板且数据是图片或文本，或者不需要发送PC剪贴板
                {
                    stream.WriteLine("Content-Length: " + sendBuf.Length);
                    stream.WriteLine();
                    stream.Write(sendBuf, 0, sendBuf.Length);
                    stream.WriteLine();
                }
                else
                {
                    long fileLen = new FileInfo(sendFile).Length;
                    stream.WriteLine("Content-Length: " + fileLen);
                    string fileName = Path.GetFileName(sendFile);
                    stream.WriteLine("Content-Disposition: attachment;filename=\"" + fileName + "\"");
                    stream.WriteLine();

                    FileStream fstream = File.OpenRead(sendFile);
                    sendBuf = new byte[4096];
                    while (fileLen > 0)
                    {
                        int readLen = fstream.Read(sendBuf, 0, (int)Math.Min(sendBuf.Length, fileLen));
                        stream.Write(sendBuf, 0, readLen);
                        fileLen -= readLen;
                        notifyIcon.Text = "还需要发送：" + (fileLen / 1024 / 1024).ToString() + " MB";
                    }
                    stream.Write(sendBuf, 0, sendBuf.Length);
                    fstream.Close();
                    stream.WriteLine();
                }

                notifyIcon.Text = Text;
            }
            catch (Exception err)
            {
                notifyIcon.Text = "错误：" + err.Message;
            }
            finally
            {
                notifyIcon.Icon = greenIcon;
                stream.Close();
                tcpClient.Close();
            }
        }
    }
}
