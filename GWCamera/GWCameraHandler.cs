using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GWCamera
{
    public class GWCameraHandler
    {
        #region 单例模式
        private static readonly GWCameraHandler instance = null;
        static GWCameraHandler()
        {
            instance = new GWCameraHandler();
        }
        private GWCameraHandler()
        {
            ZKGWLineCameraHandler.Instance.GetBarCodeEvent += UpdateBarCodeContent;
            Thread th = new Thread((SendBarcode));
            th.Start();
        }
        /// <summary>
        /// 单例模式
        /// </summary>
        public static GWCameraHandler Instance
        {
            get
            {
                return instance;
            }
        }
        #endregion

        public delegate void GetBarCodeInfoEventHandler(string[] barCode, byte[] image, double st);
        /// <summary>
        /// 得到相机信息
        /// </summary>
        public event GetBarCodeInfoEventHandler GetBarCodeInfoEvent;

        string[] MDWSCode = null;

        string[] KSJCode = null;
        string lineCode = "";
        double sendCodeTime = 0;
        public void UpdateBarcode(string[] barCode, byte[] image, double st,int cameraStyle)
        {
            sendCodeTime = st;
            if (cameraStyle == 0)
            {
                MDWSCode = barCode;
            }
            if (cameraStyle == 1)
            {
                KSJCode = barCode;
            }
        }

        /// <summary>
        /// 单号内容显示
        /// </summary>
        /// <param name="value"></param>
        public void UpdateBarCodeContent(string value, int time)
        {
            lineCode = value;
        }

        bool isWait = true;
        /// <summary>
        /// mdws跟ksj组合相机发送条码
        /// </summary>
        public void SendBarcode()
        {
            try
            {
                while (isWait)
                {
                    if (MDWSCode != null && KSJCode != null)
                    {
                        string[] barCode = MDWSCode.Union(KSJCode).ToArray<string>();
                        barCode = AddLineCode(barCode);
                        string[] newarr = null;
                        #region 多条码的时候去除noread
                        GWCameraLog.Instance.CameraRecogLog("GWCameraHandler----->" + barCode);
                        if (barCode.Length > 1 && barCode.Contains("noread"))
                        {
                            List<string> list = barCode.ToList();
                            list.Remove("noread");
                            newarr = list.ToArray();
                            if (GetBarCodeInfoEvent != null) GetBarCodeInfoEvent(newarr, null, sendCodeTime);
                        }
                        else
                        {
                            if (GetBarCodeInfoEvent != null) GetBarCodeInfoEvent(barCode, null, sendCodeTime);
                        }
                        #endregion
                        MDWSCode = null;
                        KSJCode = null;
                    }
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("GWCameraHandler----->" + ex.ToString());
            }
        }

        public string[] AddLineCode(string[] barCode)
        {
            List<string> list = barCode.ToList();
            if (lineCode.ToLower() == "noread" || lineCode.ToLower() == "nobar" || string.IsNullOrEmpty(lineCode))
            {
                if (list.Count > 1 && list.Contains("noread"))
                {
                    list.Remove("noread");
                }
                barCode = list.ToArray();
            }
            else
            {
                list.Add(lineCode.ToLower());
                if (list.Count > 1 && list.Contains("noread"))
                {
                    list.Remove("noread");
                }
                barCode = list.ToArray();
            }
            return barCode;
        }

        string cameraStyle = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "CameraStyle");
        public void Close()
        {
            if (cameraStyle == "2")
            {
                isWait = false;
            }
        }
    }
}
