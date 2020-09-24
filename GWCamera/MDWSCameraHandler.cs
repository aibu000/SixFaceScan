using GWCamera;
using KSJCamera;
using MVSDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using CameraHandle = System.Int32;

namespace GWCamera
{
    public class MDWSCameraHandler
    {
        #region 单例模式
        private static readonly MDWSCameraHandler instance = null;
        static MDWSCameraHandler()
        {
            instance = new MDWSCameraHandler();
        }
        private MDWSCameraHandler()
        {
            ZKGWLineCameraHandler.Instance.GetBarCodeEvent += UpdateBarCodeContent;
        }
        public static MDWSCameraHandler Instance
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

        private class AlgResource
        {
            private int m_progress_flag;
            private IntPtr m_results;

            public AlgResource(int flag, int max_width, int max_height)
            {
                m_progress_flag = flag;
                algorithm_init(flag, max_width, max_height, ref m_results);
            }

            public void Cleanup()
            {
                algorithm_release(m_progress_flag);
            }

            public AlgorithmResult[] Run(byte[] in_data, int width, int height)
            {
                algorithm_run(m_progress_flag, in_data, width, height, ref m_results);

                int readnum = Marshal.ReadInt32(m_results, 0);//读取内存偏移量数据
                int offset = sizeof(int);//偏移量，第一个条码是偏移4位（4byte的头），第二个条码是偏移4+结构体（tagAlgorithmResult）长度,类推...
                if (readnum >= 1)
                {
                    var ResultList = new List<AlgorithmResult>();
                    for (int i = 0; i < readnum; i++)
                    {
                        AlgorithmResult OneResult = new AlgorithmResult();
                        byte[] tmp_data = new byte[Marshal.SizeOf(typeof(AlgorithmResult))];
                        Marshal.Copy(m_results + offset, tmp_data, 0, tmp_data.Length);//偏移头部4位
                        OneResult = (AlgorithmResult)BytesToStruct(tmp_data, typeof(AlgorithmResult));
                        ResultList.Add(OneResult);
                        offset += tmp_data.Length;
                    }
                    return ResultList.ToArray();
                }
                else
                {
                    return null;
                }
            }
        }

        private class AlgResourceMan
        {
            private AlgResource[] m_ResourceList = new AlgResource[12];
            private bool[] m_BusyFlag = new bool[12];

            public AlgResourceMan(int max_width, int max_height)
            {
                for (int i = 0; i < m_ResourceList.Length; ++i)
                {
                    m_ResourceList[i] = new AlgResource(i, max_width, max_height);
                    m_BusyFlag[i] = false;
                }
            }

            public void Cleanup()
            {
                for (int i = 0; i < m_ResourceList.Length; ++i)
                {
                    m_ResourceList[i].Cleanup();
                }
            }

            public AlgResource GetAlg()
            {
                lock (m_ResourceList)
                {
                    for (int i = 0; i < m_ResourceList.Length; ++i)
                    {
                        if (!m_BusyFlag[i])
                        {
                            m_BusyFlag[i] = true;
                            return m_ResourceList[i];
                        }
                    }

                    return null;
                }
            }

            public void ReleaseAlg(AlgResource alg)
            {
                lock (m_ResourceList)
                {
                    for (int i = 0; i < m_ResourceList.Length; ++i)
                    {
                        if (m_ResourceList[i] == alg)
                        {
                            Debug.Assert(m_BusyFlag[i]);
                            m_BusyFlag[i] = false;
                            return;
                        }
                    }
                }
            }
        }

        AlgResourceMan m_AlgResourceMan;

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
        //string IsAddQueue = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "IsAddQueue");

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

        ThreadedTaskProcessor m_SaveImageProcessor = new ThreadedTaskProcessor(2);


        /// <summary>
        /// 线扫相机数据
        /// </summary>
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

        int max_width = 0;
        int max_height = 0;
        /// <summary>
        /// 相机初始化
        /// </summary>
        public void init()
        {
            try
            {
                MvApi.CameraEnumerateDevice(out m_DevInfo);
                int NumDev = m_DevInfo.Length;
                GWCameraLog.Instance.InfoLog(String.Format("{0} Device Found.", NumDev));
                cameraNum = NumDev;
                if (GetCameraNumEvent != null) GetCameraNumEvent(NumDev.ToString());

                tSdkImageResolution psCurVideoSize;
                for (int i = 0; i < NumDev; ++i)
                {
                    if (MvApi.CameraGrabber_Create(out m_Grabber[i], ref m_DevInfo[i]) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                    {
                        GWCameraLog.Instance.InfoLog("***** Open " + MvApi.CStrToString(m_DevInfo[i].acFriendlyName) + " *****");
                        //LogText(string.Format("Open Camera: {0}\n", MvApi.CStrToString(m_DevInfo[i].acFriendlyName)));
                        MvApi.CameraGrabber_GetCameraHandle(m_Grabber[i], out m_hCamera[i]);
                        //MvApi.CameraCreateSettingPage(m_hCamera[i], this.Handle, m_DevInfo[i].acFriendlyName, null, (IntPtr)0, 0); //相机属性配置窗口
                        MvApi.CameraGetImageResolution(m_hCamera[i], out psCurVideoSize);
                        if (psCurVideoSize.iWidth > max_width)
                            max_width = psCurVideoSize.iWidth;
                        if (psCurVideoSize.iHeight > max_height)
                            max_height = psCurVideoSize.iHeight;
                        // 黑白相机设置ISP输出灰度图像
                        // 彩色相机ISP默认会输出BGR24图像
                        tSdkCameraCapbility cap;
                        MvApi.CameraGetCapability(m_hCamera[i], out cap);
                        if (cap.sIspCapacity.bMonoSensor != 0)
                            MvApi.CameraSetIspOutFormat(m_hCamera[i], (uint)MVSDK.emImageFormat.CAMERA_MEDIA_TYPE_MONO8);

                        MvApi.CameraSetMirror(m_hCamera[i], 0, 1);//CameraSetMirror(hCamera, 0: 水平   1: 垂直,    0: 不翻转    1：翻转)
                        MvApi.CameraPlay(m_hCamera[i]);

                        if (MvApi.CameraConnectTest(m_hCamera[i]) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                        {
                            GetCameraState(true);
                        }
                        else
                        {
                            GetCameraState(false);
                        }
                    }
                }

                m_AlgResourceMan = new AlgResourceMan(max_width, max_height);

                var CamList = new List<int>();
                for (int i = 0; i < NumDev; ++i)
                {
                    if (m_hCamera[i] > 0)
                        CamList.Add(m_hCamera[i]);
                    #region 设置相机开始及结束的触发延时时间
                    SetCameraFPS(m_hCamera[i], 12);//固定相机帧率12帧
                    //SetCameraTriggerStartDelayTime(m_hCamera[i], (uint)int.Parse(startCameraDelayTime[i]) * 1000);
                    //SetCameraTriggerEndDelayTime(m_hCamera[i], (uint)int.Parse(endCameraDelayTime[i]) * 1000);
                    #endregion
                }

                m_Grab = new GrabQueue(CamList.ToArray(),m_DevInfo);
                m_Grab.Start();

                m_Quit = false;
                m_ProcessThread = new Thread(ProcessThread);
                m_ProcessThread.Start();

                //if (IsAddQueue == "1")
                {
                    m_SaveImageProcessor.Start();
                }

                //m_CameraConThread = new Thread(CameraConThread);
                //m_CameraConThread.Start();

                //saveNoreadPicTh = new Thread(SaveNoreadPic); //开启线程
                //saveNoreadPicTh.Start();
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("init:" + ex.ToString());
            }
        }

        /// <summary>
        /// 获取相机状态
        /// </summary>
        /// <param name="cameraID"></param>
        /// <param name="state"></param>
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

        bool SetCameraFPS(int hCamera, UInt32 fps)
        {
            return MvApi.CameraSpecialControl(hCamera, 32, fps, IntPtr.Zero) == 0;
        }

        /// <summary>
        /// 单号内容显示
        /// </summary>
        /// <param name="value"></param>
        public void UpdateBarCodeContent(string value, int time)
        {
            lineCode = value;
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

        private class ThreadedTaskBarcode : ThreadedTask
        {
            public delegate void CompleteCallback(ThreadedTaskBarcode task);

            private AlgResourceMan m_ResourceMan;
            private GrabQueue.Frame m_frame;
            private AlgorithmResult[] m_results;
            private bool m_Completed;
            private int m_UseTime;
            private CompleteCallback m_CompleteCallback;

            public ThreadedTaskBarcode(AlgResourceMan ResourceMan, GrabQueue.Frame frame, CompleteCallback callback)
            {
                m_ResourceMan = ResourceMan;
                m_frame = frame;
                m_results = null;
                m_Completed = false;
                m_UseTime = 0;
                m_CompleteCallback = callback;
            }

            public void Process()
            {
                System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
                watch.Reset();
                watch.Start();

                AlgResource alg = m_ResourceMan.GetAlg();
                m_results = alg.Run(m_frame.data, m_frame.head.iWidth, m_frame.head.iHeight);
                m_ResourceMan.ReleaseAlg(alg);

                watch.Stop();
                m_UseTime = (int)watch.ElapsedMilliseconds;
                m_Completed = true;

                if (m_CompleteCallback != null)
                {
                    m_CompleteCallback(this);
                }
            }

            public GrabQueue.Frame GetFrame()
            {
                return m_frame;
            }

            public AlgorithmResult[] GetResults()
            {
                return m_results;
            }

            public bool IsCompleted()
            {
                return m_Completed;
            }

            public int GetUseTime()
            {
                return m_UseTime;
            }
        }

        private void SaveImages(int PackID, List<ThreadedTaskBarcode> TaskList)
        {
            foreach (var task in TaskList)
            {
                var frame = task.GetFrame();
                m_SaveImageProcessor.AddTask(new ThreadedTaskSaveImage(PackID, frame.name, frame.data, 
                    frame.head.iWidth, frame.head.iHeight));
            }
        }
        /// <summary>
        /// 发送给LPS识别的图像
        /// </summary>
        byte[] sendReadBuffer = null;
        /// <summary>
        /// 发送给LPS未识别的图像
        /// </summary>
        byte[] sendNoreadBuffer = null;
        private void ProcessThread()
        {
            DateTime startTime = DateTime.Now;
            DateTime endTime = DateTime.Now;
            ThreadedTaskProcessor TaskProcessor = null;
            List<ThreadedTaskBarcode> TaskList = null;

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
                                    GWCameraLog.Instance.CameraRecogLog(string.Format("Package Begin: {0}.", e.packID));
                                    sendReadBuffer = null;
                                    sendNoreadBuffer = null;
                                    TaskList = new List<ThreadedTaskBarcode>();
                                    TaskProcessor = new ThreadedTaskProcessor();
                                    TaskProcessor.Start();
                                    
                                    startTime = e.Time;//包裹触碰到光电的时间
                                    //LogText(string.Format("Package Begin: {0}\n", e.packID));
                                }
                                break;
                            case GrabQueue.PACK_FRAME_EVENT:
                                {
                                    ThreadedTaskBarcode task = new ThreadedTaskBarcode(m_AlgResourceMan, e.frame,
                                        (ThreadedTaskBarcode t) => {
                                            if (OutputMode == "0")
                                            {
                                                var results = t.GetResults();
                                                if (results != null)
                                                {
                                                    foreach (var result in results)
                                                    {
                                                        string barCode = System.Text.Encoding.Default.GetString(result.strCodeData).TrimEnd('\0');
                                                        GWCameraLog.Instance.CameraRecogLog(string.Format("MDWSCameraHandler----->Camera {1} recognize barcode:{0}.",
                                                        barCode, MvApi.CStrToString(m_DevInfo[t.GetFrame().hCamera - 1].acFriendlyName)));
                                                        string[] str = new string[] { barCode };
                                                        #region 新增的将灰度图转成rgb
                                                        Bitmap IMG = NewBitmapFromGrayData(t.GetFrame().data, max_width, max_height);
                                                        //IMG = ZoomImage(IMG, IMG.Width / 3 * 2, IMG.Height / 3 * 2);
                                                        MemoryStream ms = new MemoryStream();
                                                        IMG.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                                                        byte[] buffer = ms.GetBuffer();
                                                        ms.Close();
                                                        #endregion
                                                        if (GetImgInfoEvent != null) GetImgInfoEvent(str, buffer, 0);
                                                    }
                                                }
                                            }
                                        });
                                    TaskProcessor.AddTask(task);
                                    
                                    TaskList.Add(task);
                                    GWCameraLog.Instance.CameraRecogLog(string.Format("Submit Frame PackID:{0} FrameID:{1}", e.packID, e.frame.id));
                                }
                                break;
                            case GrabQueue.PACK_END_EVENT:
                                {
                                    GWCameraLog.Instance.CameraRecogLog(string.Format("MDWSCameraHandler----->Package End: {0}.", e.packID));
                                    GWCameraLog.Instance.CameraRecogLog(string.Format("PackID:{0} Wait TaskProcessor Stop.", e.packID));
                                    TaskProcessor.Stop();
                                    TaskProcessor = null;
                                    GWCameraLog.Instance.CameraRecogLog("TaskProcessor Stopped.");
                                    int nFrameProcessed = 0;
                                    List<string> BarCode = new List<string>();
                                    for (int i = 0; i < TaskList.Count; ++i)
                                    {
                                        ThreadedTaskBarcode task = TaskList[i];

                                        if (task.IsCompleted() )
                                        {
                                            ++nFrameProcessed;
                                            var Results = task.GetResults();
                                            if (Results != null)
                                            {
                                                foreach(var result in Results)
                                                {
                                                    string barCode = System.Text.Encoding.Default.GetString(result.strCodeData).TrimEnd('\0');
                                                    //GWCameraLog.Instance.CameraRecogLog(string.Format("MDWSCameraHandler----->Camera {1} recognize barcode:{0}.", 
                                                    //    barCode, MvApi.CStrToString(m_DevInfo[task.GetFrame().hCamera - 1].acFriendlyName)));
                                                    if (!BarCode.Contains(barCode))
                                                    {
                                                        BarCode.Add(barCode);
                                                    }
                                                    sendReadBuffer = task.GetFrame().data;
                                                }
                                            }
                                            else
                                            {
                                                sendNoreadBuffer = task.GetFrame().data;
                                                // 这张图没有扫到码
                                            }
                                        }
                                        else
                                        {
                                            // 这张图没有运行算法
                                        }
                                        GWCameraLog.Instance.CameraRecogLog(MvApi.CStrToString(m_DevInfo[task.GetFrame().hCamera - 1].acFriendlyName) + " alg time: " + task.GetUseTime().ToString());
                                    }
                                    GWCameraLog.Instance.CameraRecogLog(string.Format("end task, {0} Frames run algorithm", nFrameProcessed));
                                    #region 狂扫模式下保存图片
                                    if (OutputMode == "0")
                                    {
                                        if (BarCode.Count == 0 && isSaveNoreadPic == "1")
                                        {
                                            SaveImages(e.packID, TaskList);
                                        }
                                    }
                                    #endregion
                                    endTime = DateTime.Now;
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
                                                //Bitmap IMG = NewBitmapFromGrayData(sendReadBuffer, max_width, max_height);
                                                ////IMG.Save(string.Format(@"D:\1.jpg"), ImageFormat.Jpeg);
                                                //MemoryStream ms = new MemoryStream();
                                                //IMG.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                                                //byte[] buffer = ms.GetBuffer();  
                                                //ms.Close();
                                                if (GetImgInfoEvent != null) GetImgInfoEvent(AddLineCode(str), null, time);
                                                //BytesToImage(buffer).Save(string.Format(@"D:\1.jpg"), ImageFormat.Jpeg);
                                            }
                                            if (cameraStyle == "2")
                                            {
                                                GWCameraHandler.Instance.UpdateBarcode(str, null, time, 0);
                                            }
                                            GWCameraLog.Instance.CameraRecogLog("MDWSCameraHandler ----->" + st.TotalMilliseconds.ToString() + "++++ from PACK_END_EVENT to send barcode" + st0.TotalMilliseconds.ToString());
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
                                                //Bitmap IMG = NewBitmapFromGrayData(sendReadBuffer, max_width, max_height);
                                                ////IMG.Save(string.Format(@"D:\1.jpg"), ImageFormat.Jpeg);
                                                //MemoryStream ms = new MemoryStream();
                                                //IMG.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                                                //byte[] buffer = ms.GetBuffer();  //byte[]   bytes=   ms.ToArray(); 这两句都可以，至于区别么，下面有解释
                                                //ms.Close();
                                                if (GetImgInfoEvent != null) GetImgInfoEvent(AddLineCode(str1), null, time);
                                                //BytesToImage(buffer).Save(string.Format(@"D:\1.jpg"), ImageFormat.Jpeg);
                                            }
                                            if (cameraStyle == "2")
                                            {
                                                GWCameraHandler.Instance.UpdateBarcode(str1, null, time, 0);
                                            }

                                            //if (GetImgInfoEvent != null) GetImgInfoEvent(str1, null, time);
                                            //GWCameraHandler.Instance.UpdateBarcode(str1, null, time,1);
                                            GWCameraLog.Instance.CameraRecogLog("MDWSCameraHandler ----->" + st.TotalMilliseconds.ToString() + "++++ from PACK_END_EVENT to send barcode" + st0.TotalMilliseconds.ToString());
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

                                            //SaveImages(e.packID, TaskList);
                                            
                                            if (cameraStyle == "0")
                                            {
                                                //Bitmap IMG = NewBitmapFromGrayData(sendNoreadBuffer, max_width, max_height);
                                                ////IMG.Save(string.Format(@"D:\1.jpg"), ImageFormat.Jpeg);
                                                //MemoryStream ms = new MemoryStream();
                                                //IMG.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                                                //byte[] buffer = ms.GetBuffer();  //byte[]   bytes=   ms.ToArray(); 这两句都可以，至于区别么，下面有解释
                                                //ms.Close();
                                                if (GetImgInfoEvent != null) GetImgInfoEvent(AddLineCode(str1), null, time);
                                                //BytesToImage(buffer).Save(string.Format(@"D:\1.jpg"), ImageFormat.Jpeg);
                                            }
                                            if (cameraStyle == "2")
                                            {
                                                GWCameraHandler.Instance.UpdateBarcode(str1, null, time, 0);
                                            }

                                            if (isSaveNoreadPic == "1" && AddLineCode(str1)[0] == "noread")//开了保存未识别图像功能已经线扫也不识别才进行保存
                                            {
                                                SaveImages(e.packID, TaskList);
                                            }

                                            GWCameraLog.Instance.CameraRecogLog("MDWSCameraHandler ----->" + st.TotalMilliseconds.ToString() + "++++ from PACK_END_EVENT to send barcode" + st0.TotalMilliseconds.ToString());
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
                        GWCameraLog.Instance.ExceptionInfoLog("ProcessThread:" + ex.ToString());
                    }
                }
            }
        }

        private Bitmap ZoomImage(Bitmap bitmap, int destHeight, int destWidth)
        {
            try
            {
                System.Drawing.Image sourImage = bitmap;
                int width = 0, height = 0;
                //按比例缩放             
                int sourWidth = sourImage.Width;
                int sourHeight = sourImage.Height;
                if (sourHeight > destHeight || sourWidth > destWidth)
                {
                    if ((sourWidth * destHeight) > (sourHeight * destWidth))
                    {
                        width = destWidth;
                        height = (destWidth * sourHeight) / sourWidth;
                    }
                    else
                    {
                        height = destHeight;
                        width = (sourWidth * destHeight) / sourHeight;
                    }
                }
                else
                {
                    width = sourWidth;
                    height = sourHeight;
                }
                Bitmap destBitmap = new Bitmap(destWidth, destHeight);
                Graphics g = Graphics.FromImage(destBitmap);
                g.Clear(Color.Transparent);
                //设置画布的描绘质量           
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(sourImage, new Rectangle((destWidth - width) / 2, (destHeight - height) / 2, width, height), 0, 0, sourImage.Width, sourImage.Height, GraphicsUnit.Pixel);
                g.Dispose();
                //设置压缩质量       
                System.Drawing.Imaging.EncoderParameters encoderParams = new System.Drawing.Imaging.EncoderParameters();
                long[] quality = new long[1];
                quality[0] = 100;
                System.Drawing.Imaging.EncoderParameter encoderParam = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                encoderParams.Param[0] = encoderParam;
                sourImage.Dispose();
                return destBitmap;
            }
            catch
            {
                return bitmap;
            }
        }  

        /// <summary>
        /// Convert Byte[] to Image
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public static Image BytesToImage(byte[] buffer)
        {
            MemoryStream ms = new MemoryStream(buffer);
            Image image = System.Drawing.Image.FromStream(ms);
            return image;
        }


        /// <summary>
        /// 将灰度图数据转换成新的bitmap图像
        /// </summary>
        /// <param name="ImgData"></param>
        /// <param name="w"></param>
        /// <param name="h"></param>
        /// <returns></returns>
        private static Bitmap NewBitmapFromGrayData(byte[] ImgData, int w, int h)
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

        internal IntPtr ArrayToIntptr(byte[] source)
        {
            if (source == null)
                return IntPtr.Zero;
            byte[] da = source;
            IntPtr ptr = Marshal.AllocHGlobal(da.Length);
            Marshal.Copy(da, 0, ptr, da.Length);
            return ptr;
        }

        public IntPtr BytesToIntptr(byte[] bytes)
        {
            int size = bytes.Length;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, 0, buffer, size);
                return buffer;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        public string[] AddLineCode(string[] barCode)
        {
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

        public void SaveNoreadPicWhenProcessEnding()
        {
            //saveNoreadPicTh = new Thread(SaveNoreadPic); //开启线程
            //saveNoreadPicTh.Start();
            //SaveNoreadPic();
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
            if (cameraStyle == "0")
            {
                if (cameraNum > 0)
                {
                    if (m_ProcessThread != null)
                    {
                        m_Quit = true;
                        m_ProcessThread.Join();
                        m_ProcessThread = null;
                    }

                    m_Grab.Stop();

                    for (int i = 0; i < cameraNum; ++i)
                    {
                        if (m_Grabber[i] != IntPtr.Zero)
                            MvApi.CameraGrabber_Destroy(m_Grabber[i]);
                    }                    
                }
                //if (IsAddQueue == "1")
                {
                    m_SaveImageProcessor.Stop();
                }
                m_AlgResourceMan.Cleanup();
            }
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
            public DateTime Time;
        }

        public class Frame
        {
            public int hCamera;
            public byte[] data;
            public tSdkFrameHead head;
            public int id;
            public string name;
        }

        private Object m_PackLock = new Object();
        private int m_PackIndex = 0;
        private Pack m_CurrentPack;
        private List<Event> m_EventList = new List<Event>();
        private EventWaitHandle m_EventListNotify = new EventWaitHandle(false, EventResetMode.ManualReset);
        tSdkCameraDevInfo[] n_DevInfo = null;

        public GrabQueue(int[] CamList, tSdkCameraDevInfo[] m_DevInfo)
        {
            n_DevInfo = m_DevInfo;
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
            m_TriggerLevelCheckThread.Start(m_CamList[0]);
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
                        if (m_CurrentPack != null && m_CurrentPack.frameCount < 100)//只会处理前100张图像（如果现场出现卡包了一直拍照未识别的情况下会占用很多资源）
                        {
                            var NewFrame = new Frame();
                            NewFrame.hCamera = hCamera;
                            NewFrame.data = buffer;
                            NewFrame.head = FrameHead;
                            NewFrame.id = m_CurrentPack.frameCount;
                            NewFrame.name = MvApi.CStrToString(n_DevInfo[hCamera - 1].acFriendlyName);
                            m_CurrentPack.frameCount++;
                            NewEvent(PACK_FRAME_EVENT, NewFrame, m_CurrentPack.id, DateTime.Now);
                        }
                    }
                }
            }
        }

        private void NewEvent(int type, Frame NewFrame, int PackID,DateTime time)
        {
            var e = new Event();
            e.type = type;
            e.packID = PackID;
            e.frame = NewFrame;
            e.Time = time;
            m_EventList.Add(e);
            m_EventListNotify.Set();
        }

        private void OnNewPack()
        {
            m_CurrentPack = new Pack();
            m_CurrentPack.id = ++m_PackIndex;

            NewEvent(PACK_BEGIN_EVENT, null, m_CurrentPack.id, DateTime.Now);
            Console.WriteLine(string.Format("Package New, id: {0}", m_CurrentPack.id));
        }

        private void OnEndPack()
        {
            Console.WriteLine(string.Format("Package Complete, id: {0}, FrameCount: {1}",
                m_CurrentPack.id, m_CurrentPack.frameCount));
            GWCameraLog.Instance.CameraRecogLog(string.Format("Package Complete, id: {0}, FrameCount: {1}",
                m_CurrentPack.id, m_CurrentPack.frameCount));
            NewEvent(PACK_END_EVENT, null, m_CurrentPack.id, DateTime.Now);
            m_CurrentPack = null;
        }

        string mySqlXmlPath = "/Config/cameraParam/";
        string clearTime = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "delayTime");
        string mdwsLevelSignal = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "mdwsLevelSignal");
        private void ReadTriggerLevelThread(Object arg)
        {
            int hCamera = (int)arg;
            uint lastState = 0xAA;

            #region
            DateTime endTime = DateTime.Now;
            #endregion

            while (!m_Quit)
            {
                uint state = 0;
                if (MvApi.CameraGetIOState(hCamera, 0, ref state) == CameraSdkStatus.CAMERA_STATUS_SUCCESS)
                {
                    if (lastState != 0xAA && lastState != state)
                    {
                        // 电平变化
                        if (lastState != int.Parse(mdwsLevelSignal))
                        {
                            GWCameraLog.Instance.CameraRecogLog("New package start : 1 -> 0");
                            //Thread.Sleep(int.Parse(startCameraDelayTime));//延迟一段时间将图像缓存清空
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
                            GWCameraLog.Instance.CameraRecogLog("the package leave!!!");
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
}

public interface ThreadedTask
{
    void Process();
}

public class ThreadedTaskSaveImage : ThreadedTask
{
    private int m_PackID;
    private string name;
    private byte[] m_Data;
    private int m_width;
    private int m_height;

    public ThreadedTaskSaveImage(int packID, string frameID, byte[] data, int width, int height)
    {
        m_PackID = packID;
        name = frameID;
        m_Data = data;
        m_width = width;
        m_height = height;
    }

    string noreadSavePicPath = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "noRecPicPath");

    public void Process()
    {
        try
        {
            string path = noreadSavePicPath + m_PackID + "\\";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            string szFileName = path + string.Format("Serials" + "{6}" + " " + "{0}-{1}-{2}-{3}-{4}-{5}.jpg", DateTime.Now.Month, DateTime.Now.Day,
                DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second, DateTime.Now.Millisecond, name);
            //var Image = NewBitmapFromGrayData(m_Data, m_width, m_height);
            //Image.Save(szFileName);

            KSJApiBase.KSJ_HelperSaveToJpg(m_Data, m_width, m_height, 8, 50, szFileName);

            //var Image = NewBitmapFromGrayData(m_Data, m_width, m_height);
            //Image.Save(szFileName, System.Drawing.Imaging.ImageFormat.Jpeg);
        }
        catch (Exception ex)
        {
            GWCameraLog.Instance.ExceptionInfoLog("Process: " + ex.ToString());
        }
    }

    /// <summary>
    /// 将灰度图数据转换成新的bitmap图像
    /// </summary>
    /// <param name="ImgData"></param>
    /// <param name="w"></param>
    /// <param name="h"></param>
    /// <returns></returns>
    private static Bitmap NewBitmapFromGrayData(byte[] ImgData, int w, int h)
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
}

public class ThreadedTaskProcessor
{
    private Queue<ThreadedTask> mTaskQ = new Queue<ThreadedTask>();
    private int mMaxQSize = int.MaxValue;
    private EventWaitHandle mQEvent = new EventWaitHandle(false, EventResetMode.ManualReset);
    private EventWaitHandle mQuitEvent = new EventWaitHandle(false, EventResetMode.ManualReset);
    private List<Thread> mWorkThreads;
    private int mThreadsNum = System.Environment.ProcessorCount;

    //public ThreadedTaskProcessor(int nMaxTask = int.MaxValue)
    //{
    //    mMaxQSize = nMaxTask;
    //}

    /// <summary>
    /// 根据传过来的nThread的值确定线程个数
    /// </summary>
    /// <param name="nThread"></param>
    /// <param name="nMaxTask"></param>
    public ThreadedTaskProcessor(int nThread = 0, int nMaxTask = int.MaxValue)
    {
        if (nThread > 0)
        {
            mThreadsNum = nThread;
        }
        mMaxQSize = nMaxTask;
    }

    public void Start()
    {
        if (mWorkThreads != null)
            return;

        mWorkThreads = new List<Thread>();
        mQuitEvent.Reset();
        for (int i = 0; i < mThreadsNum; ++i)
        {
            var thread = new Thread(WorkProc);
            thread.Start();
            mWorkThreads.Add(thread);
        }
    }

    public void Stop()
    {
        if (mWorkThreads != null)
        {
            mQuitEvent.Set();
            foreach (var thread in mWorkThreads)
            {
                thread.Join();
            }
            mWorkThreads = null;
        }
    }

    public void Clear()
    {
        lock (mTaskQ)
        {
            mTaskQ.Clear();
        }
    }

    public bool AddTask(ThreadedTask task)
    {
        lock (mTaskQ)
        {
            if (mTaskQ.Count < mMaxQSize)
            {
                mTaskQ.Enqueue(task);
                mQEvent.Set();
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    private void WorkProc()
    {
        while (true)
        {
            if (WaitHandle.WaitAny(new WaitHandle[] { mQuitEvent, mQEvent }) == 0)
                break;

            ThreadedTask task;
            lock (mTaskQ)
            {
                if (mTaskQ.Count < 1)
                {
                    mQEvent.Reset();
                    continue;
                }
                task = mTaskQ.Dequeue();
            }

            task.Process();
        }
    }
}
