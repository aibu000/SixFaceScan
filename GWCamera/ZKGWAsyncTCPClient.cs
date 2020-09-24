using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GWCamera
{
    /*
     * add by wx 2017-07-7
     * 提供socket异步接收数据、发送数据
     * 提供信息提示功能
     * socket外部连接时会将现有的守护程序关闭，内部自连接时，如果网络异常，守护程序会启动重连
     * 守护重连时间大概为4-5S，每次KeepAlive探活时间为2S，检测到没有ACK时，每个0.5秒再探活一次，连续3次。
     */

    /// <summary>
    /// TCP异步通讯类
    /// </summary>
    public class ZKGWAsyncTCPClient
    {
        #region delegate event

        #region 获取到信息
        public delegate void GetValueEventHandler(byte[] value);
        /// <summary>
        /// 获取到有效数据
        /// </summary>
        /// <param name="state"></param>
        public event GetValueEventHandler GetValueEvent;
        #endregion

        #region 信息提示
        public delegate void HintEventHandler(string hintInfo);
        public event HintEventHandler HintEvent;
        #endregion

        #region 网络状态
        public delegate void UpdateNetStateEventHandler(bool? state);
        /// <summary>
        /// 更新网络状态
        /// </summary>
        /// <param name="state">true：正常、false：异常、null：连接中</param>
        public event UpdateNetStateEventHandler UpdateNetStateEvent;
        #endregion

        
        #endregion

        #region object
        Socket socketClient = null;
        Thread checkConnectTh = null;
        /// <summary>
        /// 定义一个CountdownEvent信号类，等待把当前任务线程结束掉的完成信号
        /// </summary>
        public CountdownEvent _taskThreadOverCountDown = null;
        #endregion

        #region variables
        string ip = string.Empty;
        int port = 0;
        /// <summary>
        /// 标识是否需要检查socket状态，false时关闭当前的守护程序
        /// </summary>
        bool isNeedCheck = false;
        #endregion

        /// <summary>
        /// 开启断线重连的守护程序
        /// </summary>
        /// <param name="isReConnection"></param>
        private void StartCheckSocketTh(bool isReConnection)
        {
            if (!isReConnection)
            {
                checkConnectTh = new Thread(CheckSocketCon);
                checkConnectTh.IsBackground = true;
                checkConnectTh.Start();
            }
        }

        /// <summary>
        /// 检测socke连接状态，断开即重连
        /// </summary>
        private void CheckSocketCon()
        {
            Hint(string.Format("Demon Program start success..."));
            isNeedCheck = true;
            while (isNeedCheck)
            {
                Thread.Sleep(1000);
                if (socketClient != null && (socketClient.Connected == false || !PingIP(ip)))
                {
                    Hint(string.Format("check socket connect state {0}，start reconnect...", socketClient.Connected));
                    if (UpdateNetStateEvent != null) UpdateNetStateEvent(false);
                    AsynConnect(ip, port, true);
                    continue;
                }
                UpdateNetState(true);                
            }
            Hint(string.Format("Demon Program close success..."));
           // if (_taskThreadOverCountDown != null) _taskThreadOverCountDown.Signal();
            UpdateNetState(false);    
        }

        /// <summary>
        /// 检查目标主机的网络状态
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        private bool PingIP(object ip)
        {
            bool saveState = false;//翻转量，用来判断失败后是否重连上
            Ping ping = new Ping();
            PingReply pingReply = null;
            try
            {
                pingReply = ping.Send((string)ip);
            }
            catch (Exception ex)
            {
                saveState = false;
            }
            if (pingReply != null && pingReply.Status == IPStatus.Success)
            {
                saveState = true;
            }
            else
            {
                saveState = false;
            }
            return saveState;
        }

        /// <summary>
        /// 连接到服务器 
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <param name="isReConnection">标识是否是自动重连</param>
        public void AsynConnect(string ip, int port, bool isReConnection = false)
        {
            Close(isReConnection);
            this.ip = ip;
            this.port = port;
            //端口及IP  
            IPEndPoint ipe = new IPEndPoint(IPAddress.Parse(ip), port);
            //创建套接字  
            socketClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socketClient.IOControl(IOControlCode.KeepAliveValues, KeepAlive(1, 2000, 500), null);//设置Keep-Alive参数
            //开始连接到服务器  
            //UpdateNetState(null);
            socketClient.BeginConnect(ipe, asyncResult =>
            {
                if (socketClient != null && socketClient.Connected)
                {
                    try
                    {
                        socketClient.EndConnect(asyncResult);
                        //接受消息  
                        AsynRecive();
                        StartCheckSocketTh(isReConnection);
                        if (HintEvent != null) HintEvent(string.Format("LineCamera connect success..."));
                    }
                    catch (Exception ex)
                    {
                        Hint(string.Format("AsynConnect may exist exception:{0}...", ex.ToString()));
                        UpdateNetState(false);
                    }
                }
                else
                {
                    Hint(string.Format("connect failed,please check IP and Port of TCP Server and start state."));
                    UpdateNetState(false);
                }
            }, null);
        }

        /// <summary>
        /// KeepAlive心跳参数设置
        /// </summary>
        /// <param name="onOff">是否开启KeepAlive</param>
        /// <param name="keepAliveTime">开始首次KeepAlive探测前的TCP空闭时间，即如果keepalive心跳包被ack了则，每次间隔此时间</param>
        /// <param name="keepAliveInterval">两次KeepAlive探测间的时间间隔，如果keepalive心跳没有收到ack，则间隔此时间</param>
        /// <returns></returns>
        private byte[] KeepAlive(int onOff, int keepAliveTime, int keepAliveInterval)
        {
            byte[] buffer = new byte[12];
            BitConverter.GetBytes(onOff).CopyTo(buffer, 0);
            BitConverter.GetBytes(keepAliveTime).CopyTo(buffer, 4);
            BitConverter.GetBytes(keepAliveInterval).CopyTo(buffer, 8);
            return buffer;
        }


        /// <summary>  
        /// 发送消息  
        /// </summary>  
        /// <param name="socket"></param>  
        /// <param name="message"></param>  
        public void AsynSend(byte[] data)
        {
            if (socketClient != null)
            {
                try
                {
                    if (socketClient != null && socketClient.Connected)
                    {
                        socketClient.BeginSend(data, 0, data.Length, SocketFlags.None, asyncResult =>
                        {
                            int length = socketClient.EndSend(asyncResult);
                        }, null);
                    }
                }
                catch (Exception ex)
                {
                    Hint(string.Format("AsynSend exist exception:{0}...", ex.ToString()));
                }
            }
            else Hint(string.Format("AsynSend->socketClient is null."));
        }

        /// <summary>  
        /// 接收消息  
        /// </summary>  
        /// <param name="socket"></param>  
        public void AsynRecive()
        {
            byte[] data = new byte[1024];
            try
            {
                //开始接收数据 
                if (socketClient != null && socketClient.Connected)
                {
                    socketClient.BeginReceive(data, 0, data.Length, SocketFlags.None, asyncResult =>
                    {
                        if (socketClient != null && socketClient.Connected)
                        {
                            try
                            {
                                int length = socketClient.EndReceive(asyncResult);
                                if (length > 0)
                                {
                                    byte[] recvDest = new byte[length];
                                    Array.Copy(data, recvDest, length);
                                    if (GetValueEvent != null) GetValueEvent(recvDest);
                                    AsynRecive();
                                }
                                else
                                {
                                    //socket连接已断开
                                    AsynConnect(ip, port);
                                }

                            }
                            catch (Exception ex)
                            {
                                Hint(string.Format("AsynRecive exist excepyion:{0}...", ex.ToString()));
                            }
                        }
                    }, null);
                }
            }
            catch (Exception ex)
            {
                Hint(string.Format("AsynRecive exist exception:{0}...", ex.ToString()));
            }
        }

        /// <summary>
        /// 关闭当前连接
        /// </summary>
        public void Close(bool isReConnection = false)
        {
            if (socketClient != null)
            {
                socketClient.Close();
                socketClient = null;
                if (!isReConnection && checkConnectTh != null && checkConnectTh.IsAlive == true)
                {
                   // _taskThreadOverCountDown = new CountdownEvent(1);
                    isNeedCheck = false;
                    Console.WriteLine("go into waiting signal of thread...");
                    Hint(string.Format("go into waiting signal of thread..."));
                   // _taskThreadOverCountDown.Wait();//等待守护线程也关闭
                }
                Hint(string.Format("close connect success..."));
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// 更新网络状态
        /// </summary>
        /// <param name="state"></param>
        private void UpdateNetState(bool? state)
        {
            if (UpdateNetStateEvent != null) UpdateNetStateEvent(state);
            Console.WriteLine("*****************************" + state.ToString());
        }

        /// <summary>
        /// 提示信息
        /// </summary>
        /// <param name="msg"></param>
        private void Hint(string msg)
        {
            if (HintEvent != null) HintEvent(msg);
        }

    }
}
