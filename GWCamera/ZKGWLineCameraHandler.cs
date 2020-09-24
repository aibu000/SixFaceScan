using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GWCamera
{
    public class ZKGWLineCameraHandler
    {
        #region delegate event

        #region 网络状态
        public delegate void UpdateNetStateEventHandler(bool? state);
        /// <summary>
        /// 更新网络状态
        /// </summary>
        /// <param name="state"></param>
        public static event UpdateNetStateEventHandler UpdateNetStateEvent;
        #endregion

        #region 获取到条码信息
        public delegate void GetBarCodeEventHandler(string value, int time);
        /// <summary>
        /// 获取到有效数据
        /// </summary>
        /// <param name="state"></param>
        public event GetBarCodeEventHandler GetBarCodeEvent;
        #endregion

        public delegate void GetLineCameraInfoEventHandler(string value);
        /// <summary>
        /// 获取到有效数据
        /// </summary>
        /// <param name="state"></param>
        public event GetLineCameraInfoEventHandler GetLineCameraInfoEvent;
        #endregion

        #region object
        ZKGWAsyncTCPClient asyncTCPClient = null;
        object obj = new object();
        #endregion

        #region variable
        /// <summary>
        /// 条码数据缓冲区，存放接收到的有效数据
        /// </summary>
        public string barCodeBuffer = string.Empty;

        /// <summary>
        /// 存放最新接收到的有效数据
        /// </summary>
        // public string newBarCode = string.Empty;

        /// <summary>
        /// 二维码中识别的目的地编码
        /// </summary>
        public string destCodeBuffer = string.Empty;
        /// <summary>
        /// 上一次获取的条码值，用于当相机出现异常连续发送2次相同条码值时，避免后续的数据错误
        /// </summary>
        private string priorBarCodeBuffer = string.Empty;
        /// <summary>
        /// 接收数据缓冲区，会从此缓冲区中进行目标数据的提取
        /// </summary>
        public string recvBuffer = string.Empty;
        /// <summary>
        /// 完整的目标字符串头部标识
        /// </summary>
        private string headerFlag = "7B";// "{"字符->16进制为7B
        /// <summary>
        /// 完整的目标字符串尾部标识
        /// </summary>
        private string tailFlag = "7D";//"}"
        /// <summary>
        /// 远程IP
        /// </summary>
        private string remoteIp = "";

        /// <summary>
        /// 需要接收的图像高度
        /// </summary>
        private int imageHight = 0;
        /// <summary>
        /// 需要接收的图像宽度
        /// </summary>
        private int imageWidth = 0;
        /// <summary>
        /// 待接收图像的大小
        /// </summary>
        private int imageLen = 0;
        /// <summary>
        /// OCR参数
        /// </summary>
        private int ocrPara = 0;

        private string noReadType = "noread";
        #endregion

        #region 单例模式
        private static readonly ZKGWLineCameraHandler instance = null;
        static ZKGWLineCameraHandler()
        {
            instance = new ZKGWLineCameraHandler();
        }
        private ZKGWLineCameraHandler()
        {
        }
        public static ZKGWLineCameraHandler Instance
        {
            get
            {
                return instance;
            }
        }
        #endregion

        string hostip = XmlHelper.Instance.GetXMLInformation("/Config/LineCameraParam/" + "IP");
        string port = XmlHelper.Instance.GetXMLInformation("/Config/LineCameraParam/" + "Port");
        /// <summary>
        /// 连接相机socket
        /// </summary>
        /// <param name="hostip"></param>
        /// <param name="port"></param>
        public void Connect()
        {
            this.remoteIp = hostip;
            Init();
            asyncTCPClient.AsynConnect(hostip, int.Parse(port));
        }

        /// <summary>
        /// 初始化
        /// </summary>
        private void Init()
        {
            if (asyncTCPClient == null)
            {
                asyncTCPClient = new ZKGWAsyncTCPClient();
                asyncTCPClient.UpdateNetStateEvent += new ZKGWAsyncTCPClient.UpdateNetStateEventHandler(UpdateNetState);
                asyncTCPClient.HintEvent += new ZKGWAsyncTCPClient.HintEventHandler(ShowHint);
                asyncTCPClient.GetValueEvent += new ZKGWAsyncTCPClient.GetValueEventHandler(GetValue);
            }
        }

        private void ShowHint(string msg)
        {
            GWCameraLog.Instance.InfoLog("CameraHandler:" + msg);
            //if(GetLineCameraInfoEvent !=null) GetLineCameraInfoEvent(msg);
        }

        /// <summary>
        /// 资源释放
        /// </summary>
        public void Close()
        {
            if (asyncTCPClient != null) asyncTCPClient.Close();
        }

        /// <summary>
        /// 更新网络状态
        /// </summary>
        /// <param name="state"></param>
        private void UpdateNetState(bool? state)
        {
            if (UpdateNetStateEvent != null) UpdateNetStateEvent(state);
        }

        /// <summary>
        /// 获取到有效数据
        /// </summary>
        /// <param name="value"></param>
        private void GetValue(byte[] value)
        {
            string recvData = ToHexString(value);
            lock (obj)
            {
                try
                {
                    recvBuffer += recvData;
                    string barCode = DataHandling(ref recvBuffer, ref imageWidth, ref imageHight, ref ocrPara, ref imageLen);
                    if (!string.IsNullOrEmpty(barCode)) recvBuffer = null;
                    Console.WriteLine(string.Format("CameraHandler({0}) receive value:{1},analysis barCode:{2}", this.remoteIp, Encoding.ASCII.GetString(HexStringToByte(recvData)), barCode));
                    GWCameraLog.Instance.CameraRecogLog(string.Format("CameraHandler({0}) receive value:{1},analysis barCode:{2}", this.remoteIp, Encoding.ASCII.GetString(HexStringToByte(recvData)), barCode));

                    #region 不做任何处理  直接传出条码
                    string data = Encoding.ASCII.GetString(HexStringToByte(recvData));
                    string getBarCode = data.Substring(1, data.IndexOf("_[") - 1);
                    string getTime = data.Substring(Encoding.ASCII.GetString(HexStringToByte(recvData)).IndexOf("_[") + 2, data.IndexOf("]") - data.IndexOf("_[") - 2);
                    if (GetBarCodeEvent != null) GetBarCodeEvent(getBarCode, int.Parse(getTime));
                    byte[] returnData = { 0xAC };
                    asyncTCPClient.AsynSend(returnData);
                    #endregion
                }
                catch (Exception ex)
                {
                    GWCameraLog.Instance.ExceptionInfoLog("ZKGWLineCameraHandler----->" + "GetValue(byte[] value):" + ex.ToString());
                }
                //UpdateBarCode(barCode);
            }
        }

        /// <summary>
        /// 更新单号
        /// </summary>
        /// <param name="barCode">单号</param>
        /// <param name="isAuxiliaryData">是否是辅助数据，即辅设备获取的数据</param>
        //public void UpdateBarCode(string barCode, bool isAuxiliaryData = false)
        //{
        //    try
        //    {
        //        if (barCode != "" && barCode != noReadType && double.Parse(barCode) > 0 && this.priorBarCodeBuffer != barCode)
        //        {
        //            if (this.barCodeBuffer == "" || this.barCodeBuffer == noReadType)
        //            {
        //                this.priorBarCodeBuffer = this.barCodeBuffer = barCode;
        //                //LogHelper.Instance.Info(string.Format("use barCode：{0}", this.barCodeBuffer));
        //                if (GetBarCodeEvent != null) GetBarCodeEvent(this.barCodeBuffer);
        //            }
        //        }

        //    }
        //    catch
        //    {
        //        this.barCodeBuffer = "";
        //    }
        //}

        /// <summary>
        /// 对socket接收的数据进行规则处理，解析出正确的条码
        /// </summary>
        /// <param name="recvBarCodeBuffer">接收缓冲区数据字符串</param>
        /// <param name="imageW">要接收的图像宽</param>
        /// <param name="imageH">要接收的图像高</param>
        /// <param name="ocrExtraPara">ocr参数</param>
        /// <param name="imageDataLen">要接收的图像长度</param>
        /// <param name="priorBarCode">之前的面单号</param>
        /// <param name="headerFlag">标志头</param>
        /// <param name="tailFlag">标志尾</param>
        /// <returns></returns>
        public string DataHandling(ref string recvBarCodeBuffer, ref int imageW, ref int imageH, ref int ocrExtraPara, ref int imageDataLen)
        {
            string barCode = string.Empty;
            try
            {
                while (recvBarCodeBuffer != null)
                {
                    if (imageDataLen == 0)
                    {
                        int m_head = recvBarCodeBuffer.IndexOf(headerFlag);
                        int m_tail = recvBarCodeBuffer.IndexOf(tailFlag);
                        if (m_head != -1 && m_tail != -1)
                        {
                            if (m_tail > m_head)
                            {
                                string destValue = recvBarCodeBuffer.Substring(m_head, m_tail - m_head + 2);
                                byte[] sourceByte = HexStringToByte(destValue);
                                destValue = Encoding.ASCII.GetString(sourceByte);
                                recvBarCodeBuffer = recvBarCodeBuffer.Remove(0, m_tail + 2);
                                barCode = GetBarCode(destValue, ref imageW, ref imageH, ref ocrExtraPara, ref imageDataLen);
                                if (imageDataLen > 0) //需要接收的图像长度》0,说明需要进一步接收和解析图像数据
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                recvBarCodeBuffer = recvBarCodeBuffer.Remove(0, m_head);
                            }
                        }
                    }
                    else if (recvBarCodeBuffer.Length >= imageDataLen * 2)//一个字符转16进制字符串变成2个字符
                    {
                        string imageDataStr = recvBarCodeBuffer.Substring(0, imageDataLen * 2);
                        recvBarCodeBuffer = recvBarCodeBuffer.Remove(0, imageDataStr.Length);
                        imageDataLen = 0;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("ZKGWLineCameraHandler->DataHandling has exception:" + ex.ToString());
            }
            return barCode;
        }


        /// <summary>
        /// 从相机传出的单号数据中分析出条码单号
        /// </summary>
        /// <param name="sourceValue"></param>
        /// <param name="imageDataLen"></param>
        /// <returns></returns>
        private string GetBarCode(string sourceValue, ref int imageW, ref int imageH, ref int ocrExtraPara, ref int imageDataLen)
        {
            string barCode = string.Empty;
            string valueList = sourceValue.Replace("{", "").Replace("}", "").Replace(" ", "");
            List<string> tempBarcodeList = valueList.Split(',').ToList();
            foreach (string newBarcodeItem in tempBarcodeList)
            {
                if (!string.IsNullOrEmpty(newBarcodeItem))
                {
                    if ("NEEDOCR" == newBarcodeItem && tempBarcodeList.Count >= 4)//4E4545444F4352->"NEEDOCR"
                    {
                        /*获取到需要进行ORC图像解析条码，数据格式：{NEEDOCR,000001A0,00000019,00436595a}*/
                        try
                        {
                            //解析OCR参数：从16进制字符串“aabbccdd”中解析得到int数值，0xaabbccdd
                            imageW = Convert.ToInt32(tempBarcodeList[1], 16);//图像宽
                            imageH = Convert.ToInt32(tempBarcodeList[2], 16);//图像高
                            ocrExtraPara = Convert.ToInt32(tempBarcodeList[3], 16);//ocr算法参数
                            imageDataLen = imageW * imageH;
                        }
                        catch { }
                    }
                    else
                    {
                        imageDataLen = 0;
                        barCode = newBarcodeItem.ToLower();
                    }
                    break;
                }
            }
            return barCode;
        }

        /// <summary>
        /// byte[]->十六进制字符串
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public string ToHexString(byte[] bytes) // 0xae00cf => "AE00CF "
        {
            string hexString = string.Empty;
            if (bytes != null)
            {
                StringBuilder strB = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    strB.Append(bytes[i].ToString("X2"));
                }
                hexString = strB.ToString();
            } return hexString;
        }

        /// <summary>
        /// 十六进制字符串->byte[]
        /// </summary>
        /// <param name="hex"></param>
        /// <returns></returns>
        public byte[] HexStringToByte(string hex)
        {
            int len = (hex.Length / 2);
            byte[] result = new byte[len];
            char[] achar = hex.ToCharArray();
            for (int i = 0; i < len; i++)
            {
                int pos = i * 2;
                result[i] = (byte)(ToByte(achar[pos]) << 4 | ToByte(achar[pos + 1]));
            }
            return result;
        }

        /// <summary>
        /// char->int
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        public int ToByte(char c)
        {
            byte b = (byte)"0123456789ABCDEF".IndexOf(c);
            return b;
        }

        /// <summary>
        /// 清除缓冲区中数据
        /// </summary>
        public void ClearBuffer()
        {
            barCodeBuffer = string.Empty;
            destCodeBuffer = string.Empty;
            recvBuffer = string.Empty;
        }
    }
}
