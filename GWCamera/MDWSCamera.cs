using MVSDK;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CameraHandle = System.Int32;

namespace GWCamera
{
    public class MDWSCamera
    {
        #region 单例模式
        private static readonly MDWSCamera instance = null;
        static MDWSCamera()
        {
            instance = new MDWSCamera();
        }
        private MDWSCamera()
        {
            ZKGWLineCameraHandler.Instance.GetBarCodeEvent += UpdateBarCodeContent;
            //m_FrameCallback = new pfnCameraGrabberFrameCallback(CameraGrabberFrameCallback);
            //m_AsyncSave.Start();
        }
        public static MDWSCamera Instance
        {
            get
            {
                return instance;
            }
        }
        #endregion

        public delegate void GetCameraNumEventHandler(string cameraNum);
        /// <summary>
        /// 相机个数
        /// </summary>
        public event GetCameraNumEventHandler GetCameraNumEvent;

        /// <summary>
        /// 获取相机状态
        /// </summary>
        /// <param name="cameraID"></param>
        /// <param name="state"></param>
        public delegate void GetCameraStateEventHandler(bool state);
        public event GetCameraStateEventHandler GetCameraStateEvent;


        public delegate void GetImgInfoEventHandler(string[] barCode, byte[] image, double st);
        /// <summary>
        /// 得到相机信息
        /// </summary>
        public event GetImgInfoEventHandler GetImgInfoEvent;

        object obj = new object();

        protected Thread sendBarcodeTh;          //发送条码线程
        protected bool m_bExitsendBarcodeTh = false;//采用线程采集时，让线程退出的标志


        protected Thread algDealTh;          //算法处理线程
        protected bool m_bExitalgDealTh = false;//采用线程采集时，让线程退出的标志


        #region 算法
        [StructLayout(LayoutKind.Sequential)]
        struct AlgorithmParamSet
        {
            public int nFlag;
            public int nCodeCount;  		// 待识别条码数: 0-无限制, >0-规定个数(须小于MAX_BARCODE_COUNT给定值，当前为128)

            public int nCodeSymbology;		// 条码类型: 0-无限制, (1<<0)-code128, (1<<1)-code39, (1<<2)-code93, (1<<3)-交插25, (1<<4)-EAN13;
            //   使用"按位或"来组合多种类型, 如((1<<0) | (1<<1) | (1<<4)) = (ode128 + code39 + EAN13)

            public int nCodeDgtNum;		// 解码结果字符串位数: 0-无限制,算法对结果位数(<=32)无限定;
            //    >0-算法对结果位数(<=32)进行限定，最多支持32种不同的位数(1~32)
            //    (1<<(n-1)) = 支持结果字符串位数为n的条码输出
            //    使用"按位或"来组合多种类型
            //    如设置输出条码限定为1位、12位及32位三种，可令nCodeDgtNum=(1<<0) | (1<<11) | (1<<31)

            public int nCodeDgtNumExt;		// 解码结果字符串位数扩充，是[nCodeDgtNum]在大于32位时的扩充，预留待用，当前无功能，默认为0

            public int nCodeValidity;	 	// 字符有效性: 0-无限制, (1<<0)-数字(ASCII 48~57), (1<<1)-小写字母(ASCII 97~122), (1<<2)-大写字母(ASCII 65~90)
            //   (1<<3)-"space"(ASCII 32), (1<<4)-"!"(ASCII 33), (1<<5)-'"'(ASCII 34), (1<<6)-"#"(ASCII 35),
            //   (1<<7)-"$"(ASCII 36), (1<<8)-"%"(ASCII 37), (1<<9)-"&"(ASCII 38), (1<<10)-"'"(ASCII 39),
            //   (1<<11)-"("和")"(ASCII 40~41), (1<<12)-"*"(ASCII 42), (1<<13)-"+"(ASCII 43), (1<<14)-","(ASCII 44),
            //   (1<<15)-"-"(ASCII 45), (1<<16)-"."(ASCII 46), (1<<17)-"/"(ASCII 47), (1<<18)-":"(ASCII 58),
            //   (1<<19)-";"(ASCII 59), (1<<20)-"<"和">"(ASCII 60,62), (1<<21)-"="(ASCII 61), (1<<22)-"?"(ASCII 63),
            //   (1<<23)-"@"(ASCII 64), (1<<24)-"["和"]"(ASCII 91,93), (1<<25)-"\"(ASCII 92), (1<<26)-"^"(ASCII 94),
            //   (1<<27)-"_"(ASCII 95), (1<<28)-"`"(ASCII 96), (1<<29)-"{"和"}"(ASCII 123,125), (1<<30)-"|"(ASCII 124),
            //   (1<<31)-"~"(ASCII 126)
            //   使用"按位或"来组合多种字符类型, 如((1<<0) | (1<<1) | (1<<2)) = 支持包含数字、小写字母以及大写字母的条码结果输出，其余都视为非法字符进行过滤

            public int nCodeValidityExt;  	// 字符有效性扩充，预留待用，当前无功能，默认为0

            public int nMultiPkgDetect;  	// 多包裹预警开关: 0-关闭; 非0-开启

        }

        [StructLayout(LayoutKind.Sequential)]
        // 2- 算法结果输出结构体
        public struct AlgorithmResult
        {
            public int nFlag;				// 结构体标志位，为1时当前结构体节点信息（结果）有效
            public int nCodeSymbology;	// 条形码类型，(1<<0)-code128, (1<<1)-code39, (1<<2)-code93, (1<<3)-交插25, (1<<4)-EAN13;
            public int nCodeCharNum;		// 条形码结果字符位数
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public byte[] strCodeData;	   // 条形码解码结果
            public int ptCodeCenter;		// 条形码中心坐标，高16位（short类型）:X横轴坐标，低16位（short类型）:Y纵轴坐标；坐标值有可能为负数
            public int ptCodeBound1;		// 条形码顶点坐标1，高16位（short类型）:X横轴坐标，低16位（short类型）:Y纵轴坐标；坐标值有可能为负数
            public int ptCodeBound2;		// 条形码顶点坐标2，高16位（short类型）:X横轴坐标，低16位（short类型）:Y纵轴坐标；坐标值有可能为负数
            public int ptCodeBound3;		// 条形码顶点坐标3，高16位（short类型）:X横轴坐标，低16位（short类型）:Y纵轴坐标；坐标值有可能为负数
            public int ptCodeBound4;		// 条形码顶点坐标4，高16位（short类型）:X横轴坐标，低16位（short类型）:Y纵轴坐标；坐标值有可能为负数
            public int nCodeOrient;		// 条形码位姿角度，0 ~ 359°
            public int nCodeWidth;		// 条形码宽度，以像素为单位
            public int nCodeHeight;		// 条形码高度，以像素为单位
            public int nCodeModuleWid;	// 条形码单位模块宽度，为扩大1024倍后的值，像素为单位
            public int nCodeSeqNum;		// 当前条形码序号
            public int reserve0;  		// 预留，当前无功能
            public int reserve1;	 	// 预留，当前无功能
            public int reserve2;  		// 预留，当前无功能
            public int reserve3;  		// 预留，当前无功能
        }


        [DllImport("AlgorithmBarcodeDetection64.dll", EntryPoint = "algorithm_init", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        static extern int algorithm_init(int progress_flag, int max_width, int max_height, ref IntPtr results);

        [DllImport("AlgorithmBarcodeDetection64.dll", EntryPoint = "algorithm_run", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        static extern int algorithm_run(int progress_flag, byte[] in_data, int width, int height, ref IntPtr results);

        [DllImport("AlgorithmBarcodeDetection64.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void algorithm_release(int progress_flag);

        [DllImport("AlgorithmBarcodeDetection64.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int algorithm_setparams(int progress_flag, ref AlgorithmParamSet paramset);

        [DllImport("AlgorithmBarcodeDetection64.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void algorithm_resetparams(int progress_flag);
        #endregion

        #region variable
        protected IntPtr[] m_Grabber = new IntPtr[12];
        protected CameraHandle[] m_hCamera = new CameraHandle[12];
        protected tSdkCameraDevInfo[] m_DevInfo;

        protected GrabQueue m_Grab;
        protected bool m_Quit;
        protected Thread m_ProcessThread;


        protected bool m_QuitCameraCon;
        protected Thread m_CameraConThread;


        IntPtr[] results_Camera = new IntPtr[12];

        string isSavePic = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "savePic");
        string StartCameraDelayTime = XmlHelper.Instance.GetXMLInformation("/Config/MDWSParam/" + "startCameraDelayTime");
        string EndCameraDelayTime = XmlHelper.Instance.GetXMLInformation("/Config/MDWSParam/" + "endCameraDelayTime");
        string OutputMode = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "OutputMode");
        string TriggerMode = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "TriggerMode");
        string isOpenAlg = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "IsOpenAlg");
        string cameraStyle = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "CameraStyle");

        string savePicPath = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "recPicPath");
        string noreadSavePicPath = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "noRecPicPath");
        string isSaveNoreadPic = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "saveNoreadPic");
        /// <summary>
        /// 开始拍照的延时时间
        /// </summary>
        string[] startCameraDelayTime = new string[12];
        /// <summary>
        /// 结束拍照的延时时间
        /// </summary>
        string[] endCameraDelayTime = new string[12];
        /// <summary>
        /// 相机个数
        /// </summary>
        public int cameraNum = 0;
        #endregion

        #region 保存未识别图片
        /// <summary>
        /// 保存不识别的图片线程
        /// </summary>
        Thread saveNoreadPicTh = null;
        bool n_exisrSaveNoreadPicTh = false;
        //Stack<List<SavePicParam>> noReadCameraBufferQ = new Stack<List<SavePicParam>>();
        Stack<SavePicParam> noReadCameraBufferQ1 = new Stack<SavePicParam>();
        Stack<SavePicParam> noReadCameraBufferQ2 = new Stack<SavePicParam>();
        Stack<SavePicParam> noReadCameraBufferQ3 = new Stack<SavePicParam>();
        Stack<SavePicParam> noReadCameraBufferQ4 = new Stack<SavePicParam>();
        Stack<SavePicParam> noReadCameraBufferQ5 = new Stack<SavePicParam>();
        Stack<SavePicParam> noReadCameraBufferQ6 = new Stack<SavePicParam>();
        Stack<SavePicParam> noReadCameraBufferQ7 = new Stack<SavePicParam>();
        Stack<SavePicParam> noReadCameraBufferQ8 = new Stack<SavePicParam>();
        /// <summary>
        /// 保存图片需要用到的参数
        /// </summary>
        struct SavePicParam
        {
            public int ID;
            public int hCamera;
            public List<byte[]> cameraBuffer;
            public int width;
            public int height;
        }
        #endregion
        private SavePicParam[] savePicParam;
        private bool isAddStack = false;

        private pfnCameraGrabberFrameCallback m_FrameCallback;
        private AsyncSaveImage m_AsyncSave = new AsyncSaveImage();

        string lineCode = "";

        /// <summary>
        /// 设置相机延时时间 
        /// </summary>
        void SetCameraDelayTime(int cameraNum)
        {
            string[] a = StartCameraDelayTime.Split(',');
            string[] b = EndCameraDelayTime.Split(',');
            for (int i = 0; i < cameraNum; i++)
            {
                startCameraDelayTime[i] = a[i];
                endCameraDelayTime[i] = b[i];
            }
        }

        int index_num = 0;
        private void CameraGrabberFrameCallback(
            IntPtr Grabber,
            IntPtr pFrameBuffer,
            ref tSdkFrameHead pFrameHead,
            IntPtr Context)
        {
            // 数据处理回调

            // 由于黑白相机在相机打开后设置了ISP输出灰度图像
            // 因此此处pFrameBuffer=8位灰度数据
            // 否则会和彩色相机一样输出BGR24数据

            // 彩色相机ISP默认会输出BGR24图像
            // pFrameBuffer=BGR24数据

            // 获取保存参数
            lock (obj)
            {
                try
                {
                    int Fmt = 0;
                    index_num++;
                    if (isSavePic == "1")
                    {
                        string sPath = string.Format(@"d:\GWCameraPic\");
                        if (!Directory.Exists(sPath))
                        {
                            Directory.CreateDirectory(sPath);
                        }
                        for (int i = 0; i < cameraNum; i++)
                        {
                            string filename = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                            filename = MvApi.CStrToString(m_DevInfo[i].acFriendlyName) + "_" + index_num.ToString() + "_" + filename;
                            SaveImage(CombineImageSavePath(sPath, filename), Fmt, pFrameBuffer, pFrameHead, i);
                        }
                    }
                }
                catch (Exception ex)
                {
                    GWCameraLog.Instance.ExceptionInfoLog("MDWSCameraHandler----->" + ex.ToString());
                }
            }
        }
        private bool SaveImage(string path, int FmtIndex, IntPtr pFrameBuffer, tSdkFrameHead FrameHead,int hCamera)
        {
            int Fmt = 1;
            //switch (FmtIndex)
            //{
            //    case 0:
            //        Fmt = (FrameHead.uiMediaType == (uint)MVSDK.emImageFormat.CAMERA_MEDIA_TYPE_MONO8 ? 16 : 2);
            //        break;
            //    case 1:
            //        Fmt = 8;
            //        break;
            //    case 2:
            //        Fmt = 1;
            //        break;
            //} 
            return m_AsyncSave.SaveImage(m_hCamera[hCamera], path, pFrameBuffer, ref FrameHead, (emSdkFileType)Fmt, 30);           
        }

        private string CombineImageSavePath(string dir, string filename)
        {
            if (dir.Length > 0)
            {
                try
                {
                    dir = Path.GetFullPath(dir);
                }
                catch (Exception e)
                {
                    dir = "";
                }
            }
            if (dir.Length == 0)
            {
                dir = AppDomain.CurrentDomain.BaseDirectory.ToString();
            }
            return System.IO.Path.Combine(dir, filename);
        }
        

        /// <summary>
        /// 相机初始化
        /// </summary>
        public void init()
        {
            if (cameraStyle == "0")
            {
                try
                {
                    saveNoreadPicTh = new Thread(SaveNoreadPic);
                    saveNoreadPicTh.Start();
                    MvApi.CameraEnumerateDevice(out m_DevInfo);
                    if (m_DevInfo == null)
                    {
                        return;
                    }
                    int NumDev = m_DevInfo.Length;
                    //if (GetCameraNumEvent != null) GetCameraNumEvent(string.Format("连接上{0}面相机......", NumDev.ToString()));
                    cameraNum = NumDev;

                    savePicParam = new SavePicParam[8];
                    SetCameraDelayTime(NumDev);
                    tSdkImageResolution psCurVideoSize;
                    for (int i = 0; i < NumDev; ++i)
                    {
                        if (MvApi.CameraGrabber_Create(out m_Grabber[i], ref m_DevInfo[i]) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                        {
                            GWCameraLog.Instance.InfoLog("MDWSCameraHandler----->***** Open " + MvApi.CStrToString(m_DevInfo[i].acFriendlyName) + " *****");
                            //LogText(string.Format("Open Camera: {0}\n", MvApi.CStrToString(m_DevInfo[i].acFriendlyName)));
                            MvApi.CameraGrabber_GetCameraHandle(m_Grabber[i], out m_hCamera[i]);
                            //MvApi.CameraCreateSettingPage(m_hCamera[i], this.Handle, m_DevInfo[i].acFriendlyName, null, (IntPtr)0, 0); //相机属性配置窗口
                            MvApi.CameraGetImageResolution(m_hCamera[i], out psCurVideoSize);
                            algorithm_init(i + 1, psCurVideoSize.iWidth, psCurVideoSize.iHeight, ref results_Camera[i + 1]);//自适应相机的分辨率
                            // 黑白相机设置ISP输出灰度图像
                            // 彩色相机ISP默认会输出BGR24图像
                            tSdkCameraCapbility cap;
                            MvApi.CameraGetCapability(m_hCamera[i], out cap);
                            if (cap.sIspCapacity.bMonoSensor != 0)
                                MvApi.CameraSetIspOutFormat(m_hCamera[i], (uint)MVSDK.emImageFormat.CAMERA_MEDIA_TYPE_MONO8);

                            MvApi.CameraPlay(m_hCamera[i]);

                            if (MvApi.CameraConnectTest(m_hCamera[i]) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                            {
                                //GetCameraState(i, true);
                                GetCameraState(true);
                            }
                            else
                            {
                               // GetCameraState(false);
                                GetCameraState(false);
                            }
                        }

                        //if (status == 0)
                        {
                            //MvApi.CameraGrabber_GetCameraDevInfo(m_Grabber[i], out m_DevInfo[i]);
                            //MvApi.CameraGrabber_GetCameraHandle(m_Grabber[i], out m_hCamera[i]);

                            //MvApi.CameraGrabber_SetRGBCallback(m_Grabber[i], m_FrameCallback, IntPtr.Zero);

                            //// 黑白相机设置ISP输出灰度图像
                            //// 彩色相机ISP默认会输出BGR24图像
                            //tSdkCameraCapbility cap;
                            //MvApi.CameraGetCapability(m_hCamera[i], out cap);
                            //if (cap.sIspCapacity.bMonoSensor != 0)
                            //{
                            //    MvApi.CameraSetIspOutFormat(m_hCamera[i], (uint)MVSDK.emImageFormat.CAMERA_MEDIA_TYPE_MONO8);
                            //}
                            //MvApi.CameraGrabber_StartLive(m_Grabber[i]);
                        }
                    }

                    var CamList = new List<int>();
                    for (int i = 0; i < NumDev; ++i)
                    {
                        if (m_hCamera[i] > 0)
                            CamList.Add(m_hCamera[i]);
                        #region 设置相机开始及结束的触发延时时间
                        SetCameraTriggerStartDelayTime(m_hCamera[i], (uint)int.Parse(startCameraDelayTime[i]) * 1000);
                        SetCameraTriggerEndDelayTime(m_hCamera[i], (uint)int.Parse(endCameraDelayTime[i]) * 1000);
                        #endregion
                    }

                    m_Grab = new GrabQueue(CamList.ToArray());
                    m_Grab.Start();

                    m_Quit = false;
                    m_ProcessThread = new Thread(ProcessThread);
                    m_ProcessThread.Start();

                    m_QuitCameraCon = false;
                    //m_CameraConThread = new Thread(CameraConThread);
                    //m_CameraConThread.Start();
                }
                catch (Exception ex)
                {
                    GWCameraLog.Instance.ExceptionInfoLog("MDWSCameraHandler----->init:" + ex.ToString());
                }
            }
        }

        private void CameraConThread()
        {
            try
            {
                while (!m_QuitCameraCon)
                {
                    for (int i = 0; i < 3; i++)
                    {

                        if (MvApi.CameraConnectTest(m_hCamera[i]) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                        {
                            GetCameraState(true);
                        }
                        else
                        {
                            GetCameraState(false);
                        }
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("AlgDealThread;" + ex.ToString());
            }
        }

        /// <summary>
        /// 获取相机状态
        /// </summary>
        /// <param name="cameraID"></param>
        /// <param name="state"></param>
        //private void GetCameraState(int cameraID, bool state)
        //{
        //    if (GetCameraStateEvent != null) GetCameraStateEvent(cameraID, state);
        //}
        private void GetCameraState(bool state)
        {
            if (GetCameraStateEvent != null) GetCameraStateEvent(state);
        }

        /// <summary>
        /// 设置触发启动延时
        /// </summary>
        /// <param name="hCamera"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        private bool SetCameraTriggerStartDelayTime(int hCamera, UInt32 time)
        {
            return MvApi.CameraSpecialControl(hCamera, 9, 0x1ff, new IntPtr(time)) == CameraSdkStatus.CAMERA_STATUS_SUCCESS;
        }

        
        /// <summary>
        /// 设置触发结束延时
        /// </summary>
        /// <param name="hCamera"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        private bool SetCameraTriggerEndDelayTime(int hCamera, UInt32 time)
        {
            return MvApi.CameraSpecialControl(hCamera, 9, 0x1fe, new IntPtr(time)) == CameraSdkStatus.CAMERA_STATUS_SUCCESS;
        }

        async void AsyncFunction(int packID,int hCamera,byte[] data ,int iWidth, int iHeight)
        {
            await Task.Delay(1);
            if (isSavePic == "1")
            {
                string sPath = string.Format(@"d:\GWCameraPic\");
                if (!Directory.Exists(sPath))
                {
                    Directory.CreateDirectory(sPath);
                }
                string path = sPath + packID.ToString() + "_Camera" + hCamera.ToString() + "_" + DateTime.Now.ToString("yyyyMMddhhmmssfff") + ".jpg";
                byte[] copy = new byte[data.Length];
                Array.Copy(data, copy, data.Length);
                Bitmap bmp = NewBitmapFromGrayData(copy, iWidth, iHeight);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
                bmp.Dispose();
            }
        }

        object OBJ = new object();
        private void ProcessThread()
        {
            List<string> LastBarCode = new List<string>();
            List<string> BarCode = new List<string>();
            DateTime startTime = DateTime.Now;
            DateTime endTime = DateTime.Now;
            while (!m_Quit)
            {
                var e = m_Grab.GetEvent(200);
                if (e != null)
                {
                    try
                    {
                        switch (e.type)
                        {
                            case GrabQueue.PACK_BEGIN_EVENT:
                                {
                                    for (int i = 0; i < 8; i++)
                                    {
                                        savePicParam[i].cameraBuffer = new List<byte[]>();
                                    }
                                    isAddStack = false;
                                    startTime = DateTime.Now;//包裹触碰到光电的时间
                                    GWCameraLog.Instance.CameraRecogLog(string.Format("MDWSCameraHandler----->Package Begin: {0}.", e.packID));
                                }
                                break;
                            case GrabQueue.PACK_FRAME_EVENT:
                                {
                                    try
                                    {
                                        #region 将图像数据保存至stack
                                        try
                                        {
                                            //lock (OBJ)
                                            {
                                                byte[] copy = new byte[e.frame.data.Length];
                                                Array.Copy(e.frame.data, copy, e.frame.data.Length);
                                                GWCameraLog.Instance.InfoLog("保存至savePicParam的e.frame.hCamera：" + e.frame.hCamera.ToString());
                                                savePicParam[e.frame.hCamera -1].ID = e.packID;
                                                savePicParam[e.frame.hCamera -1].hCamera = e.frame.hCamera;
                                                savePicParam[e.frame.hCamera -1].cameraBuffer.Add(copy);
                                                savePicParam[e.frame.hCamera -1].width = e.frame.head.iWidth;
                                                savePicParam[e.frame.hCamera -1].height = e.frame.head.iHeight;
                                                GWCameraLog.Instance.CameraRecogLog("give data to savePicParam : " + e.frame.hCamera.ToString() + "the size of pic:" + copy.Length.ToString());
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            GWCameraLog.Instance.ExceptionInfoLog("give data to savePicParam:" + ex.ToString());
                                        }
                                        #endregion

                                        #region 算法处理
                                        if (isOpenAlg == "0")
                                        {
                                            DateTime dt = DateTime.Now; //取当前时间
                                            algorithm_run(e.frame.hCamera, e.frame.data, e.frame.head.iWidth, e.frame.head.iHeight, ref results_Camera[e.frame.hCamera]);
                                            int readnum = Marshal.ReadInt32(results_Camera[e.frame.hCamera], 0);//读取内存偏移量数据
                                            int offset = sizeof(int);//偏移量，第一个条码是偏移4位（4byte的头），第二个条码是偏移4+结构体（tagAlgorithmResult）长度,类推...
                                            string barCode = "";
                                            #region
                                            if (readnum >= 1)
                                            {
                                                isAddStack = true;
                                                for (int i = 0; i < readnum; i++)
                                                {
                                                    AlgorithmResult m_tagAlgorithmResult = new AlgorithmResult();
                                                    byte[] m_data = new byte[Marshal.SizeOf(typeof(AlgorithmResult))];
                                                    Marshal.Copy(results_Camera[e.frame.hCamera] + offset, m_data, 0, m_data.Length);//偏移头部4位
                                                    m_tagAlgorithmResult = (AlgorithmResult)BytesToStruct(m_data, typeof(AlgorithmResult));

                                                    barCode = System.Text.Encoding.Default.GetString(m_tagAlgorithmResult.strCodeData).TrimEnd('\0');
                                                    if (OutputMode == "0")
                                                    {
                                                        string[] str = new string[] { barCode };
                                                        if (GetImgInfoEvent != null) GetImgInfoEvent(str, null, 0);
                                                    }
                                                    GWCameraLog.Instance.CameraRecogLog(string.Format("MDWSCameraHandler----->Camera {1} recognize barcode:{0}.", barCode, MvApi.CStrToString(m_DevInfo[e.frame.hCamera - 1].acFriendlyName)));
                                                    if (!BarCode.Contains(barCode))
                                                    {
                                                        BarCode.Add(barCode);
                                                    }
                                                    offset += m_data.Length;
                                                }
                                            }
                                            //else
                                            //{
                                            //    barCode = "noread";
                                            //    GWCameraLog.Instance.CameraRecogLog(string.Format("recognize barcode:{0}.", barCode));
                                            //    if (!BarCode.Contains(barCode))
                                            //    {
                                            //        BarCode.Add(barCode);
                                            //    }
                                            //}
                                            #endregion
                                            System.TimeSpan st0 = DateTime.Now.Subtract(dt);
                                            GWCameraLog.Instance.CameraRecogLog("MDWSCameraHandler----->" + MvApi.CStrToString(m_DevInfo[e.frame.hCamera - 1].acFriendlyName) + "_algorithm end,consuming------->" + st0.Milliseconds.ToString() + "!!!");
                                        }
                                        #endregion
                                    }
                                    catch (Exception EX)
                                    {
                                        GWCameraLog.Instance.ExceptionInfoLog("MDWSCameraHandler----->ProcessThread:" + EX.ToString());
                                    }
                                }
                                break;
                            case GrabQueue.PACK_END_EVENT:
                                {
                                    #region 狂扫模式下保存图片
                                    if (OutputMode == "0")
                                    {
                                        if (!isAddStack)
                                        {
                                            if (!(savePicParam[0].cameraBuffer.Count == 0))
                                            {
                                                noReadCameraBufferQ1.Push(savePicParam[0]);
                                            }
                                            if (!(savePicParam[1].cameraBuffer.Count == 0))
                                            {
                                                noReadCameraBufferQ2.Push(savePicParam[1]);
                                            }
                                            if (!(savePicParam[2].cameraBuffer.Count == 0))
                                            {
                                                noReadCameraBufferQ3.Push(savePicParam[2]);
                                            }
                                            if (!(savePicParam[3].cameraBuffer.Count == 0))
                                            {
                                                noReadCameraBufferQ4.Push(savePicParam[3]);
                                            }
                                            if (!(savePicParam[4].cameraBuffer.Count == 0))
                                            {
                                                noReadCameraBufferQ5.Push(savePicParam[4]);
                                            }
                                            if (!(savePicParam[5].cameraBuffer.Count == 0))
                                            {
                                                noReadCameraBufferQ6.Push(savePicParam[5]);
                                            } 
                                            if (!(savePicParam[6].cameraBuffer.Count == 0))
                                            {
                                                noReadCameraBufferQ7.Push(savePicParam[6]);
                                            }
                                            if (!(savePicParam[7].cameraBuffer.Count == 0))
                                            {
                                                noReadCameraBufferQ8.Push(savePicParam[7]);
                                            }
                                            GWCameraLog.Instance.InfoLog(string.Format("{0};{1};{2};{3};{4};{5};{6};{7}", savePicParam[0].cameraBuffer.Count, savePicParam[1].cameraBuffer.Count, savePicParam[2].cameraBuffer.Count, savePicParam[3].cameraBuffer.Count, savePicParam[4].cameraBuffer.Count, savePicParam[5].cameraBuffer.Count, savePicParam[6].cameraBuffer.Count, savePicParam[7].cameraBuffer.Count));
                                        }
                                    }
                                    #endregion
                                    endTime = DateTime.Now;
                                    GWCameraLog.Instance.CameraRecogLog(string.Format("MDWSCameraHandler----->Package End: {0}.", e.packID));
                                    if (OutputMode == "1")
                                    {
                                        #region
                                        if (BarCode.Count > 1)
                                        {
                                            for (int i = 0; i < BarCode.Count; i++)
                                            {
                                                BarCode.Remove("noread");
                                            }
                                            string[] str = BarCode.ToArray();
                                            for (int i = 0; i < str.Length; i++)
                                            {
                                                GWCameraLog.Instance.CameraRecogLog("MDWSCameraHandler----->this is a multi-barcode " + i.ToString() + " recognized barcode: " + str[i].ToString());
                                            }
                                            System.TimeSpan st = DateTime.Now.Subtract(startTime);
                                            System.TimeSpan st0 = DateTime.Now.Subtract(endTime);
                                            double time = double.Parse(st.TotalMilliseconds.ToString());
                                            if (cameraStyle == "0")
                                            {
                                                if (GetImgInfoEvent != null) GetImgInfoEvent(AddLineCode(str), null, time);
                                            }
                                            if (cameraStyle == "2")
                                            {
                                                GWCameraHandler.Instance.UpdateBarcode(str, null, time, 0);
                                            }
                                            GWCameraLog.Instance.InfoLog("MDWSCameraHandler ----->" + st.TotalMilliseconds.ToString() + "++++ from PACK_END_EVENT to send barcode" + st0.TotalMilliseconds.ToString());
                                            GWCameraLog.Instance.CameraRecogLog("MDWSCameraHandler----->this is a whole process ending!!!");
                                            if (BarCode.Count > 0)
                                            {
                                                BarCode.Clear();
                                            }
                                        }
                                        else if (BarCode.Count == 1)
                                        {
                                            string[] str1 = BarCode.ToArray();
                                            for (int i = 0; i < str1.Length; i++)
                                            {
                                                GWCameraLog.Instance.CameraRecogLog(i.ToString() + "recognized barcode: " + str1[i].ToString());
                                            }
                                            System.TimeSpan st = DateTime.Now.Subtract(startTime);
                                            System.TimeSpan st0 = DateTime.Now.Subtract(endTime);
                                            double time = double.Parse(st.TotalMilliseconds.ToString());

                                            if (cameraStyle == "0")
                                            {
                                                if (GetImgInfoEvent != null) GetImgInfoEvent(AddLineCode(str1), null, time);
                                            }
                                            if (cameraStyle == "2")
                                            {
                                                GWCameraHandler.Instance.UpdateBarcode(str1, null, time, 0);
                                            }

                                            //if (GetImgInfoEvent != null) GetImgInfoEvent(str1, null, time);
                                            //GWCameraHandler.Instance.UpdateBarcode(str1, null, time,1);
                                            GWCameraLog.Instance.InfoLog("MDWSCameraHandler ----->" + st.TotalMilliseconds.ToString() + "++++ from PACK_END_EVENT to send barcode" + st0.TotalMilliseconds.ToString());
                                            GWCameraLog.Instance.CameraRecogLog("MDWSCameraHandler----->this is a whole process ending!!!");
                                            //LastBarCode = BarCode;
                                            if (BarCode.Count > 0)
                                            {
                                                BarCode.Clear();
                                            }
                                        }
                                        else if (BarCode.Count == 0)
                                        {
                                            string[] str1 = { "noread" };
                                            GWCameraLog.Instance.CameraRecogLog("Don't recognized this package,the barcode is: " + "noread");
                                            System.TimeSpan st = DateTime.Now.Subtract(startTime);
                                            System.TimeSpan st0 = DateTime.Now.Subtract(endTime);
                                            double time = double.Parse(st.TotalMilliseconds.ToString());

                                            noReadCameraBufferQ1.Push(savePicParam[0]);
                                            noReadCameraBufferQ2.Push(savePicParam[1]);
                                            noReadCameraBufferQ3.Push(savePicParam[2]);
                                            noReadCameraBufferQ4.Push(savePicParam[3]);
                                            noReadCameraBufferQ5.Push(savePicParam[4]);
                                            noReadCameraBufferQ6.Push(savePicParam[5]);
                                            noReadCameraBufferQ7.Push(savePicParam[6]);
                                            noReadCameraBufferQ8.Push(savePicParam[7]);
                                            GWCameraLog.Instance.InfoLog(string.Format("{0};{1};{2};{3};{4};{5};{6};{7}", savePicParam[0].cameraBuffer.Count, savePicParam[1].cameraBuffer.Count, savePicParam[2].cameraBuffer.Count, savePicParam[3].cameraBuffer.Count, savePicParam[4].cameraBuffer.Count, savePicParam[5].cameraBuffer.Count, savePicParam[6].cameraBuffer.Count, savePicParam[7].cameraBuffer.Count));
                                            GWCameraLog.Instance.CameraRecogLog("BarCode.Count == 0-----》noReadCameraBufferQ.Push(savePicParam);the size of savePicParam:" + savePicParam.Length.ToString());
                                            if (cameraStyle == "0")
                                            {
                                                if (GetImgInfoEvent != null) GetImgInfoEvent(AddLineCode(str1), null, time);
                                            }
                                            if (cameraStyle == "2")
                                            {
                                                GWCameraHandler.Instance.UpdateBarcode(str1, null, time, 0);
                                            }
                                           
                                            //if (GetImgInfoEvent != null) GetImgInfoEvent(str1, null, time);
                                            //GWCameraHandler.Instance.UpdateBarcode(str1, null, time,1);
                                            GWCameraLog.Instance.InfoLog("MDWSCameraHandler ----->" + st.TotalMilliseconds.ToString() + "++++ from PACK_END_EVENT to send barcode" + st0.TotalMilliseconds.ToString());
                                            GWCameraLog.Instance.CameraRecogLog("MDWSCameraHandler----->this is a whole process ending!!!");
                                            //LastBarCode = BarCode;
                                            if (BarCode.Count > 0)
                                            {
                                                BarCode.Clear();
                                            }
                                        }
                                        #endregion
                                    }
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        GWCameraLog.Instance.ExceptionInfoLog("MDWSCameraHandler -----> ProcessThread:" + ex.ToString());
                    }
                }
            }
        }
        private void SaveNoreadPic()
        {
            lock (obj)
            {
                while (!n_exisrSaveNoreadPicTh)
                {
                    try
                    {
                        SavePicParam[] SavePicParam1 = noReadCameraBufferQ1.ToArray(); noReadCameraBufferQ1.Clear();
                        SavePicParam[] SavePicParam2 = noReadCameraBufferQ2.ToArray(); noReadCameraBufferQ2.Clear();
                        SavePicParam[] SavePicParam3 = noReadCameraBufferQ3.ToArray(); noReadCameraBufferQ3.Clear();
                        SavePicParam[] SavePicParam4 = noReadCameraBufferQ4.ToArray(); noReadCameraBufferQ4.Clear();
                        SavePicParam[] SavePicParam5 = noReadCameraBufferQ5.ToArray(); noReadCameraBufferQ5.Clear();
                        SavePicParam[] SavePicParam6 = noReadCameraBufferQ6.ToArray(); noReadCameraBufferQ6.Clear();
                        SavePicParam[] SavePicParam7 = noReadCameraBufferQ7.ToArray(); noReadCameraBufferQ7.Clear();
                        SavePicParam[] SavePicParam8 = noReadCameraBufferQ8.ToArray(); noReadCameraBufferQ8.Clear();
                        if (SavePicParam1.Length > 0)
                        {
                            SaveNoreadPic(SavePicParam1);
                        }
                        if (SavePicParam2.Length > 0)
                        {
                            SaveNoreadPic(SavePicParam2);
                        }
                        if (SavePicParam3.Length > 0)
                        {
                            SaveNoreadPic(SavePicParam3);
                        }
                        if (SavePicParam4.Length > 0)
                        {
                            SaveNoreadPic(SavePicParam4);
                        }
                        if (SavePicParam5.Length > 0)
                        {
                            SaveNoreadPic(SavePicParam5);
                        }
                        if (SavePicParam6.Length > 0)
                        {
                            SaveNoreadPic(SavePicParam6);
                        }
                        if (SavePicParam7.Length > 0)
                        {
                            SaveNoreadPic(SavePicParam7);
                        }
                        if (SavePicParam8.Length > 0)
                        {
                            SaveNoreadPic(SavePicParam8);
                        }
                        Thread.Sleep(20000);
                    }
                    catch (Exception ex)
                    {
                        GWCameraLog.Instance.ExceptionInfoLog("SaveNoreadPic" + ex.Message);
                    }
                }
            }
        }

        void SaveNoreadPic(SavePicParam[] savePicParam)
        {
            //lock (obj)
            {
                #region 保存图像
                if (isSaveNoreadPic == "1")
                {
                    try
                    {
                        for (int i = 0; i < savePicParam.Length; i++)
                        {
                            string path = noreadSavePicPath + savePicParam[i].ID.ToString() + "//";
                            string sPath = string.Format(@path);
                            if (!Directory.Exists(sPath))
                            {
                                Directory.CreateDirectory(sPath);
                            }

                            for (int p = 0; p < savePicParam[i].cameraBuffer.Count; p++)
                            {
                                string szFileName = sPath + string.Format("Serials{6}--{7}--{0}-{1}-{2}-{3}-{4}-{5}.bmp", DateTime.Now.Month, DateTime.Now.Day,
                                    DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second, DateTime.Now.Millisecond, MvApi.CStrToString(m_DevInfo[savePicParam[i].hCamera - 1].acFriendlyName), p.ToString());

                                FileStream fs = null;
                                fs = new FileStream(szFileName, FileMode.Create, FileAccess.ReadWrite);
                                fs.Write(savePicParam[i].cameraBuffer[p], 0, savePicParam[i].cameraBuffer[p].Length); //写入文件保存jpeg文件
                                fs.Close();
                                //Bitmap bmp = BuiltGrayBitmap(savePicParam[i].cameraBuffer[p], savePicParam[i].width, savePicParam[i].height);
                                //bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
                                //bmp.Dispose();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GWCameraLog.Instance.ExceptionInfoLog("SaveNoreadPic(SavePicParam[] savePicParam):" + ex.ToString());
                    }
                }
                #endregion
            }
        }

        /// <summary>
        /// 用灰度数组新建一个8位灰度图像。
        /// </summary>
        /// <param name="rawValues"> 灰度数组(length = width * height)。 </param>
        /// <param name="width"> 图像宽度。 </param>
        /// <param name="height"> 图像高度。 </param>
        /// <returns> 新建的8位灰度位图。 </returns>
        private Bitmap BuiltGrayBitmap(byte[] rawValues, int width, int height)
        {
            // 新建一个8位灰度位图，并锁定内存区域操作
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height),
                 ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

            // 计算图像参数
            int offset = bmpData.Stride - bmpData.Width;        // 计算每行未用空间字节数
            IntPtr ptr = bmpData.Scan0;                         // 获取首地址
            int scanBytes = bmpData.Stride * bmpData.Height;    // 图像字节数 = 扫描字节数 * 高度
            byte[] grayValues = new byte[scanBytes];            // 为图像数据分配内存

            // 为图像数据赋值
            int posSrc = 0, posScan = 0;                        // rawValues和grayValues的索引
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    grayValues[posScan++] = rawValues[posSrc++];
                }
                // 跳过图像数据每行未用空间的字节，length = stride - width * bytePerPixel
                posScan += offset;
            }

            // 内存解锁
            Marshal.Copy(grayValues, 0, ptr, scanBytes);
            bitmap.UnlockBits(bmpData);  // 解锁内存区域

            // 修改生成位图的索引表，从伪彩修改为灰度
            ColorPalette palette;
            // 获取一个Format8bppIndexed格式图像的Palette对象
            using (Bitmap bmp = new Bitmap(1, 1, PixelFormat.Format8bppIndexed))
            {
                palette = bmp.Palette;
            }
            for (int i = 0; i < 256; i++)
            {
                palette.Entries[i] = Color.FromArgb(i, i, i);
            }
            // 修改生成位图的索引表
            bitmap.Palette = palette;

            return bitmap;
        }

        public string[] AddLineCode(string[] barCode)
        {
            //List<string> list = barCode.ToList();
            //if (lineCode.ToLower() == "noread" || lineCode.ToLower() == "nobar" || string.IsNullOrEmpty(lineCode))
            //{
            //    if (list.Count > 1 && list.Contains("noread"))
            //    {
            //        list.Remove("noread");
            //    }
            //    barCode = list.ToArray();
            //}
            //else
            //{
            //    list.Add(lineCode.ToLower());
            //    if (list.Count > 1 && list.Contains("noread"))
            //    {
            //        list.Remove("noread");
            //    }
            //    barCode = list.ToArray();
            //}
            //return barCode;

            try
            {
                List<string> list = barCode.ToList();
                string[] lineCodeStr = null;
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
                    if (lineCode.Contains(","))
                    {
                        lineCodeStr = lineCode.Split(',');
                    }

                    if (lineCodeStr != null)
                    {
                        for (int i = 0; i < lineCodeStr.Length; i++)
                        {
                            list.Add(lineCodeStr[i].ToLower());
                        }
                    }
                    else
                    {
                        list.Add(lineCode.ToLower());
                    }
                    if (list.Count > 1 && list.Contains("noread"))
                    {
                        list.Remove("noread");
                    }
                    barCode = list.ToArray();
                }
                return barCode;
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("MDWSCamera----->" + ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// 将灰度图数据转换成新的bitmap图像
        /// </summary>
        /// <param name="ImgData"></param>
        /// <param name="w"></param>
        /// <param name="h"></param>
        /// <returns></returns>
        private Bitmap NewBitmapFromGrayData(byte[] ImgData, int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format8bppIndexed);

            ColorPalette GrayPal = bmp.Palette;
            for (int Y = 0; Y < GrayPal.Entries.Length; Y++)
                GrayPal.Entries[Y] = Color.FromArgb(255, Y, Y, Y);
            bmp.Palette = GrayPal;

            BitmapData bmpData = bmp.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            Marshal.Copy(ImgData, 0, bmpData.Scan0, ImgData.Length);
            bmp.UnlockBits(bmpData);
            return bmp;
        }

        /// <summary转结构体>
        /// 将byte[]
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="strcutType"></param>
        /// <returns></returns>
        public static object BytesToStruct(byte[] bytes, Type strcutType)
        {
            int size = Marshal.SizeOf(strcutType);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, 0, buffer, size);
                return Marshal.PtrToStructure(buffer, strcutType);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Close()
        {
            n_exisrSaveNoreadPicTh = true;
            GWCameraLog.Instance.InfoLog("Start to resource mdwscamera...");
            if (cameraStyle == "0")
            {
                //m_AsyncSave.Stop();
                //m_AsyncSave.Clear();
                if (cameraNum != 0)
                {
                    if (m_ProcessThread != null)
                    {
                        m_Quit = true;
                        //m_ProcessThread.Join();
                        m_ProcessThread = null;
                    }

                    m_Grab.Stop();

                    for (int i = 0; i < cameraNum; ++i)
                    {
                        algorithm_release(i + 1);
                        if (m_Grabber[i] != IntPtr.Zero)
                            MvApi.CameraGrabber_Destroy(m_Grabber[i]);
                    }
                }
            }
            GWCameraLog.Instance.InfoLog("End to resource mdwscamera...");
        }


        /// <summary>
        /// 设置触发模式
        /// </summary>
        /// <param name="hand"></param>
        /// <param name="mode"></param>
        public void SetTriggerMode(int mode)
        {
            try
            {
                for (int i = 0; i < cameraNum; ++i)
                {
                    MvApi.CameraSetTriggerMode(m_hCamera[i], mode);//0表示连续模式，1是软触发，2是硬触发。
                }
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("SetTriggerMode:" + ex.ToString());
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

        //public void LogText(string text)
        //{
        //    if (this.InvokeRequired)
        //    {
        //        this.Invoke(new MethodInvoker(() => { listBox1.Items.Add(text); }));
        //    }
        //    else
        //    {
        //        listBox1.Items.Add(text);
        //    }
        //}
    }

    public class GrabQueue
    {
        private List<int> m_CamList = new List<CameraHandle>();
        private bool m_Quit = false;
        private List<Thread> m_GrabThreadList = new List<Thread>();
        private Thread m_TriggerLevelCheckThread;

        private class Pack
        {
            public int id;
            public int frameCount = 0;
        }

        /// <summary>
        /// 开始拍照
        /// </summary>
        public const int PACK_BEGIN_EVENT = 0;
        /// <summary>
        /// 获取图像
        /// </summary>
        public const int PACK_FRAME_EVENT = 1;
        /// <summary>
        /// 拍照结束
        /// </summary>
        public const int PACK_END_EVENT = 2;

        public class Event
        {
            public int type;
            public int packID;
            public Frame frame;
        }

        public class Frame
        {
            public int hCamera;
            public byte[] data;
            public tSdkFrameHead head;
        }

        private Object m_PackLock = new Object();
        private int m_PackIndex = 0;
        private Pack m_CurrentPack;
        private List<Event> m_EventList = new List<Event>();
        private EventWaitHandle m_EventListNotify = new EventWaitHandle(false, EventResetMode.ManualReset);
        private string clearTime = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "delayTime");
        string isSavePic = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "savePic");
        string levelSignal = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "mdwsLevelSignal");
        public GrabQueue(int[] CamList)
        {
            foreach (var hCam in CamList)
            {
                m_CamList.Add(hCam);
            }
        }

        public bool Start()
        {
            if (m_TriggerLevelCheckThread != null)
                return false;

            m_Quit = false;
            foreach (var hCam in m_CamList)
            {
                var t = new Thread(GrabThread);
                t.Start(hCam);
                m_GrabThreadList.Add(t);
            }

            m_TriggerLevelCheckThread = new Thread(ReadTriggerLevelThread);
            m_TriggerLevelCheckThread.Start(m_CamList[1]);
            return true;
        }

        public void Stop()
        {
            m_Quit = true;
            if (m_TriggerLevelCheckThread != null)
            {
                m_TriggerLevelCheckThread.Join();
                m_TriggerLevelCheckThread = null;
            }
            foreach (var t in m_GrabThreadList)
            {
                t.Join();
            }
            m_GrabThreadList.Clear();
        }

        public Event GetEvent(int TimeOut)
        {
            DateTime EndTime = DateTime.Now + TimeSpan.FromMilliseconds(TimeOut);
            for (; ; )
            {
                lock (m_PackLock)
                {
                    if (m_EventList.Count > 0)
                    {
                        var e = m_EventList[0];
                        m_EventList.RemoveAt(0);
                        return e;
                    }
                    else
                    {
                        m_EventListNotify.Reset();
                    }
                }

                DateTime Current = DateTime.Now;
                if (Current >= EndTime)
                    break;
                if (!m_EventListNotify.WaitOne(EndTime - Current))
                    break;
            }
            return null;
        }

        private void GrabThread(Object arg)
        {
            int hCamera = (int)arg;
            tSdkFrameHead FrameHead;
            IntPtr uRawBuffer;//rawbuffer由SDK内部申请。应用层不要调用delete之类的释放函数
            GWCameraLog.Instance.InfoLog("start capture ----->" + hCamera.ToString());
            while (!m_Quit)
            {
                CameraSdkStatus eStatus = MvApi.CameraGetImageBuffer(hCamera, out FrameHead, out uRawBuffer, 500);
                if (eStatus == CameraSdkStatus.CAMERA_STATUS_SUCCESS)//如果是触发模式，则有可能超时
                {
                    // 分配一个缓存
                    byte[] buffer = new byte[FrameHead.uBytes];
                    GCHandle hObject = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                    IntPtr bufferPtr = hObject.AddrOfPinnedObject();
                    MvApi.CameraImageProcess(hCamera, uRawBuffer, bufferPtr, ref FrameHead);
                    MvApi.CameraReleaseImageBuffer(hCamera, uRawBuffer);

                    if (hObject.IsAllocated)
                        hObject.Free();

                    // 放到队列中
                    lock (m_PackLock) 
                    {
                        if (m_CurrentPack != null)
                        {
                            var NewFrame = new Frame();
                            NewFrame.hCamera = hCamera;
                            NewFrame.data = buffer;
                            NewFrame.head = FrameHead;
                            m_CurrentPack.frameCount++;
                            NewEvent(PACK_FRAME_EVENT, NewFrame, m_CurrentPack.id);
                            //GWCameraLog.Instance.CameraRecogLog(hCamera.ToString() +  ": Capture img");
                        }
                    }
                }
            }
        }

        private void NewEvent(int type, Frame NewFrame, int PackID)
        {
            var e = new Event();
            e.type = type;
            e.packID = PackID;
            e.frame = NewFrame;

            m_EventList.Add(e);
            m_EventListNotify.Set();
        }

        private void OnNewPack()
        {
            m_CurrentPack = new Pack();
            m_CurrentPack.id = ++m_PackIndex;

            NewEvent(PACK_BEGIN_EVENT, null, m_CurrentPack.id);
            Console.WriteLine(string.Format("Package New, id: {0}", m_CurrentPack.id));
            GWCameraLog.Instance.CameraRecogLog(string.Format("MDWSCameraHandler----->Package New, id: {0}", m_CurrentPack.id));
        }

        private void OnEndPack()
        {
            GWCameraLog.Instance.CameraRecogLog(string.Format("MDWSCameraHandler----->Package Complete, id: {0}, FrameCount: {1}.wait for a moment and clear Pack Frame!!!",
                m_CurrentPack.id, m_CurrentPack.frameCount));
            ClearPackFrame(m_CurrentPack.id);
            NewEvent(PACK_END_EVENT, null, m_CurrentPack.id);
            m_CurrentPack = null;
        }


        private void ClearPackFrame(int PackID)
        {
            m_EventList.RemoveAll(e => e.type == PACK_FRAME_EVENT && e.packID == PackID);
        }
        /// <summary>
        /// 读取触发档位的线程
        /// </summary>
        /// <param name="arg"></param>
        private void ReadTriggerLevelThread(Object arg)
        {
            int hCamera = (int)arg;
            uint lastState = 0xAA;
            
            while (!m_Quit)
            {
                uint state = 0;
                if (MvApi.CameraGetIOState(hCamera, 0, ref state) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    if (lastState != 0xAA && lastState != state)
                    {
                        // 电平变化
                        if (lastState != int.Parse(levelSignal))
                        {
                            GWCameraLog.Instance.CameraRecogLog("新包裹进入 从1 -> 0");
                            // 从1 -> 0
                            // 新包裹进入
                            lock (m_PackLock)
                            {
                                if (m_CurrentPack != null)
                                {
                                    OnEndPack();
                                }
                                OnNewPack();
                            }
                        }
                        else
                        {
                            GWCameraLog.Instance.CameraRecogLog("MDWSCameraHandler -----> the package leave!!!");
                            Thread.Sleep(int.Parse(clearTime));//延迟一段时间将图像缓存清空
                            // 从0 -> 1
                            // 包裹离开
                            lock (m_PackLock)
                            {
                                if (m_CurrentPack != null)
                                {
                                    OnEndPack();
                                }
                            }
                        }
                    }
                    lastState = state;
                }
                Thread.Sleep(1);
            }
        }
    }

    public class AsyncSaveImage
    {
        private class Item
        {
            public CameraHandle hCamera;
            public IntPtr Image;
            public string FileName;
            public emSdkFileType FileType;
            public byte Quality;
        }

        private Queue<Item> mImageQ = new Queue<Item>();
        private int mMaxQSize = 1024;
        private EventWaitHandle mQEvent = new EventWaitHandle(false, EventResetMode.ManualReset);
        private EventWaitHandle mQuitEvent = new EventWaitHandle(false, EventResetMode.ManualReset);
        private List<Thread> mSaveThreads;
        private int mThreadsNum = System.Environment.ProcessorCount;

        public AsyncSaveImage()
        {

        }

        public void Start()
        {
            if (mSaveThreads != null)
                return;

            mSaveThreads = new List<Thread>();
            mQuitEvent.Reset();
            for (int i = 0; i < mThreadsNum; ++i)
            {
                var thread = new Thread(SaveProc);
                thread.Start();
                mSaveThreads.Add(thread);
            }
        }

        public void Stop()
        {
            if (mSaveThreads != null)
            {
                mQuitEvent.Set();
                foreach (var thread in mSaveThreads)
                {
                    thread.Join();
                }
                mSaveThreads = null;
            }
        }

        public void Clear()
        {
            lock (mImageQ)
            {
                foreach (var item in mImageQ)
                {
                    MvApi.CameraImage_Destroy(item.Image);
                }
                mImageQ.Clear();
            }
        }

        public bool SaveImage(CameraHandle hCamera,
            string lpszFileName,
            IntPtr pbyImageBuffer,
            ref tSdkFrameHead pFrInfo,
            emSdkFileType byFileType,
            Byte byQuality)
        {
            IntPtr Image;
            if (MvApi.CameraImage_Create(out Image, pbyImageBuffer, ref pFrInfo, 1) != CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                return false;

            Item new_item = new Item();
            new_item.hCamera = hCamera;
            new_item.Image = Image;
            new_item.FileName = lpszFileName;
            new_item.FileType = byFileType;
            new_item.Quality = byQuality;

            lock (mImageQ)
            {
                if (mImageQ.Count < mMaxQSize)
                {
                    mImageQ.Enqueue(new_item);
                    mQEvent.Set();
                    return true;
                }
                else
                {
                    MvApi.CameraImage_Destroy(Image);
                    return false;
                }
            }
        }

        private void SaveProc()
        {
            while (true)
            {
                if (WaitHandle.WaitAny(new WaitHandle[] { mQuitEvent, mQEvent }) == 0)
                    break;

                Item item;
                lock (mImageQ)
                {
                    if (mImageQ.Count < 1)
                    {
                        mQEvent.Reset();
                        continue;
                    }
                    item = mImageQ.Dequeue();
                }

                SaveItem(item);
                MvApi.CameraImage_Destroy(item.Image);
            }
        }

        private void SaveItem(Item item)
        {
            IntPtr Image = item.Image;

            IntPtr pFrameBuffer, pHeadPtr;
            MvApi.CameraImage_GetData(Image, out pFrameBuffer, out pHeadPtr);

            tSdkFrameHead FrameHead = (tSdkFrameHead)Marshal.PtrToStructure(pHeadPtr, typeof(tSdkFrameHead));
            MvApi.CameraSaveImage(item.hCamera, item.FileName, pFrameBuffer, ref FrameHead, item.FileType, item.Quality);
        }
    }
}
