using GWCamera;
using KSJ_GS;
using KSJ_Win;
using KSJApi_Companding;
using KSJApi_Io;
using KSJApi_TriggerMode;
using KSJCamera;
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
using System.Threading.Tasks;

namespace KSJSixScans
{
    public class KsjCameraHandler
    {
        #region 单例模式
        private static readonly KsjCameraHandler instance = null;
        static KsjCameraHandler()
        {
            instance = new KsjCameraHandler();
        }
        private KsjCameraHandler()
        {
            ZKGWLineCameraHandler.Instance.GetBarCodeEvent += UpdateBarCodeContent;
        }
        /// <summary>
        /// 单例模式
        /// </summary>
        public static KsjCameraHandler Instance
        {
            get
            {
                return instance;
            }
        }
        #endregion

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

        #region event and delegate
        public delegate void GetCameraNumEventHandler(int cameraNum);
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
        #endregion

        #region variables
        /// <summary>
        /// 相机设备个数
        /// </summary>
        int m_nDeviceNum = 0;
        int m_nDeviceCurSel = -1;
        string lineCode = "";

        public DEVICEINFO[] m_DeviceInfo = new DEVICEINFO[m_nMaxDevice];
        public const int m_nMaxDevice = 64;
        public struct DEVICEINFO
        {
            public int nIndex;
            public KSJApiBase.KSJ_DEVICETYPE DeviceType;
            public int nSerials;
            public ushort wFirmwareVersion;
            public ushort wFpgaVersion;
        };

        protected GrabQueue m_Grab;
        protected bool m_Quit;
        protected Thread m_ProcessThread;

        int[] nCaptureWidth;
        int[] nCaptureHeight;
        int[] nCaptureBitCount;
        IntPtr[] results_Camera = new IntPtr[12];

        /// <summary>
        /// ksj相机高低电平有效帧率模式下的固定帧率
        /// </summary>
        string ksjFrame = XmlHelper.Instance.GetXMLInformation("/Config/KSJCameraParam/" + "frame");
        /// <summary>
        /// 凯视佳相机日志开关 0-关闭 1-打开
        /// </summary>
        string ksjLogEnable = XmlHelper.Instance.GetXMLInformation("/Config/KSJCameraParam/" + "ksjLogEnable");
        /// <summary>
        /// 凯视佳相机日志路径
        /// </summary>
        string ksjLogPath = XmlHelper.Instance.GetXMLInformation("/Config/KSJCameraParam/" + "ksjLogPath");
        string ksjExposure = XmlHelper.Instance.GetXMLInformation("/Config/KSJCameraParam/" + "ksjExposureLine");
        string delayTime = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "delayTime");
        string isSavePic = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "savePic");
        string OutputMode = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "OutputMode");
        string TriggerMode = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "TriggerMode");
        string isOpenAlg = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "IsOpenAlg");
        string cameraStyle = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "CameraStyle");
        string serials = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "Serials");
        string Filter = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "Filter");
        string Step = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "Step");
        string savePicPath = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "recPicPath");
        string noreadSavePicPath = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "noRecPicPath");
        string isSaveNoreadPic = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "saveNoreadPic");
        string LampStyle = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "LampStyle");
        //string IsAddQueue = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "IsAddQueue");
        //byte[] pImageData;
        byte[][] pImageData;
        #endregion

        ThreadedTaskProcessor m_SaveImageProcessor = new ThreadedTaskProcessor(2);
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


        /// <summary>
        /// 初始化相机
        /// </summary>
        public void InitCamera()
        {
            #region 创建凯视佳日志
            if (!Directory.Exists(ksjLogPath))
            {
                Directory.CreateDirectory(ksjLogPath);
            }
            KSJApiBase.KSJ_LogSet((ksjLogEnable == "0") ? false : true, ksjLogPath);
            #endregion

            int max_width = 0;
            int max_height = 0;
            if (cameraStyle == "1")
            {
                //m_AsyncSave.Start();
                try
                {
                    //KSJApiBase.KSJ_UnInit();
                    KSJApiBase.KSJ_Init();
                    m_nDeviceNum = KSJApiBase.KSJ_DeviceGetCount();
                    GWCameraLog.Instance.InfoLog(String.Format("{0} Device Found.", m_nDeviceNum));
                    if (m_nDeviceNum > 0)
                    {
                        if (GetCameraStateEvent != null) GetCameraStateEvent(true);
                    }
                    else
                    {
                        if (GetCameraStateEvent != null) GetCameraStateEvent(false);
                    }

                    nCaptureBitCount = new int[m_nDeviceNum];
                    nCaptureWidth = new int[m_nDeviceNum];
                    nCaptureHeight = new int[m_nDeviceNum];
                    pImageData = new byte[m_nDeviceNum][];
                    if (GetCameraNumEvent != null) GetCameraNumEvent(m_nDeviceNum);
                    if (m_nDeviceNum == 0)
                    {
                        m_nDeviceCurSel = -1;
                        return;
                    }
                    if (m_nDeviceCurSel >= m_nDeviceNum)
                    {
                        m_nDeviceCurSel = 0;
                    }
                }
                catch (Exception ex)
                {
                    GWCameraLog.Instance.ExceptionInfoLog("InitCamera----->" + ex.ToString());
                }
                try
                {
                    //saveNoreadPicTh = new Thread(SaveNoreadPic);
                    //saveNoreadPicTh.Start();
                    for (int i = 0; i < m_nDeviceNum; i++)
                    {
                        nCaptureHeight[i] = 0;
                        nCaptureWidth[i] = 0;
                        m_DeviceInfo[i].nIndex = i;
                        KSJApiBase.KSJ_DeviceGetInformationEx(i, ref m_DeviceInfo[i].DeviceType, ref m_DeviceInfo[i].nSerials, ref m_DeviceInfo[i].wFirmwareVersion, ref m_DeviceInfo[i].wFpgaVersion);
                        byte btMajVersion = (byte)((m_DeviceInfo[i].wFirmwareVersion & 0xFF00) >> 8);		// 得到主版本号
                        byte btMinVersion = (byte)(m_DeviceInfo[i].wFirmwareVersion & 0x00FF);				// 得到副版本号

                        byte btFpgaMajVersion = (byte)((m_DeviceInfo[i].wFpgaVersion & 0xFF00) >> 8);		// 得到主版本号
                        byte btFpgaMinVersion = (byte)(m_DeviceInfo[i].wFpgaVersion & 0x00FF);				// 得到副版本号
                        string szText = String.Format("Index({0})-Type({1})-Serials({2})-FwVer({3}.{4})-FpgaVer({5}.{6})",
                                                i, KSJGS.g_szDeviceType[(int)(m_DeviceInfo[i].DeviceType)], m_DeviceInfo[i].nSerials, btMajVersion, btMinVersion, btFpgaMajVersion, btFpgaMinVersion);
                        GWCameraLog.Instance.InfoLog(szText);
                        int nRet = KSJApiBase.KSJ_CaptureGetSizeEx(i, ref nCaptureWidth[i], ref nCaptureHeight[i], ref nCaptureBitCount[i]);
                        //algorithm_init(i + MDWSCameraHandler.Instance.cameraNum, nCaptureWidth[i], nCaptureHeight[i], ref results_Camera[i]);
                        pImageData[i] = new byte[nCaptureWidth[i] * nCaptureHeight[i] * (nCaptureBitCount[i] >> 3)];
                        //savePicParam[i].cameraBuffer[i] = new byte[nCaptureWidth[i] * nCaptureHeight[i] * (nCaptureBitCount[i] >> 3)];
                        max_width = nCaptureWidth[i];
                        max_height = nCaptureHeight[i];
                        if (LampStyle == "1")
                        {
                            #region 软件一开光源长亮模式
                            KSJApiTriggerMode.KSJ_SetFixedFrameRateEx(i, int.Parse(ksjFrame));
                            KSJApiTriggerMode.KSJ_TriggerModeSet(i, KSJApiTriggerMode.KSJ_TRIGGERMODE.KSJ_TRIGGER_HIGHLOWFIXFRAMERATE);//设置相机外高低电平触发
                            KSJApiTriggerMode.KSJ_TriggerMethodSet(i, KSJApiTriggerMode.KSJ_TRIGGERMETHOD.KSJ_TRIGGER_HIGHLEVEL);//设置相机高电平模式
                            KSJApiIo.KSJ_FlashControlSet(i, false, false, 0);//控制光源开关
                            byte b = 0x02;
                            KSJApiIo.KSJ_GpioSetDirection(i, b);
                            KSJApiTriggerMode.KSJ_CaptureSetTimeOut(i, 0xFFFFFFFF);
                            KSJApiIo.KSJ_GpioOutModeSet(i, KSJApi_Io.KSJApiIo.KSJ_GPIOOUT_MODE.KSJ_GPIOOUT_NORMAL);
                            KSJApiIo.KSJ_GpioSetStatus(i, 0x02);//低电平到高电平光源长亮
                            #endregion
                        }

                        if (LampStyle == "0")
                        {
                            #region 高低电平控制光源长亮
                            SetGpioDirection(i, 1, true);// 设置IO1为输出，控制闪光灯
                            KSJApiIo.KSJ_GpioInModeSet(i, KSJApiIo.KSJ_GPIOIN_MODE.KSJ_GPIOIN_NORMAL);// 设置GPIO为输入输出正常模式
                            KSJApiIo.KSJ_GpioOutModeSet(i, KSJApiIo.KSJ_GPIOOUT_MODE.KSJ_GPIOOUT_NORMAL);
                            // XIIX 设置滤波
                            KSJApiIo.KSJ_GpioFilterSet(i, ushort.Parse(Filter));  // us为单位 5MS
                            // XIIX 设置延时，保证等已经打开后才开始采集（原来出现问题就是因为信高电平后立即采集，软件开灯晚了）
                            KSJApiTriggerMode.KSJ_TriggerDelaySet(i, ushort.Parse(Step)); // 100us为步长  10MS
                            #endregion

                            #region 设置曝光行数以及触发模式
                            // WLB Add...
                            KSJApiTriggerMode.KSJ_TriggerModeSet(i, KSJApiTriggerMode.KSJ_TRIGGERMODE.KSJ_TRIGGER_HIGHLOWFIXFRAMERATE);//设置相机高低电平有效固定帧率                       
                            KSJApiTriggerMode.KSJ_SetFixedFrameRateEx(i, int.Parse(ksjFrame));
                            KSJApiTriggerMode.KSJ_TriggerMethodSet(i, KSJApiTriggerMode.KSJ_TRIGGERMETHOD.KSJ_TRIGGER_HIGHLEVEL);//设置相机高电平模式                        
                            KSJApiTriggerMode.KSJ_CaptureSetTimeOut(i, 0xFFFFFFFF);
                            KSJApiIo.KSJ_GpioOutModeSet(i, KSJApi_Io.KSJApiIo.KSJ_GPIOOUT_MODE.KSJ_GPIOOUT_NORMAL);
                            #endregion
                        }

                    }
                }
                catch (Exception ex)
                {
                    GWCameraLog.Instance.ExceptionInfoLog("InitCamera: " + ex.ToString());
                }
                var CamList = new List<int>();
                for (int i = 0; i < m_nDeviceNum; ++i)
                {
                    CamList.Add(i);
                }
                m_Grab = new GrabQueue(CamList.ToArray(), nCaptureWidth, nCaptureHeight, nCaptureBitCount);
                m_Grab.Start();

                m_Quit = false;
                m_ProcessThread = new Thread(ProcessThread);
                m_ProcessThread.Start();

                //GWCameraLog.Instance.InfoLog(max_width.ToString() + "++++++++++++" + max_height.ToString());
                m_AlgResourceMan = new AlgResourceMan(max_width, max_height);
                //if (IsAddQueue == "1")
                {
                    m_SaveImageProcessor.Start();
                }
            }
        }

        #region add by gjb 高低电平控制光源长亮
        private bool SetGpioDirection(int nCamareIndex, int nPinIndex, bool bOutput)			// 1 - 输出，0 - 输入
        {
            if (nCamareIndex == -1) return false;
            if (nPinIndex >= 8 || nPinIndex < 0) return false;

            byte btDirection = 0;
            int nRet = KSJApiIo.KSJ_GpioGetDirection(nCamareIndex, ref btDirection);
            if (nRet != 1) return false;

            byte btMask = (byte)(0x01 << nPinIndex);

            if (bOutput) btDirection |= btMask;
            else btDirection &= (byte)(~btMask);

            nRet = KSJApiIo.KSJ_GpioSetDirection(nCamareIndex, btDirection);

            return nRet == 1;
        }

        private bool SetGpioStatus(int nCamareIndex, int nPinIndex, bool bLevel)
        {
            if (nCamareIndex == -1) return false;
            if (nPinIndex >= 8 || nPinIndex < 0) return false;

            byte btStatus = 0;
            int nRet = KSJApiIo.KSJ_GpioGetStatus(nCamareIndex, ref btStatus);
            if (nRet != 1) return false;

            byte btMask = (byte)(0x01 << nPinIndex);

            if (bLevel) btStatus |= btMask;
            else btStatus &= (byte)(~btMask);

            nRet = KSJApiIo.KSJ_GpioSetStatus(nCamareIndex, btStatus);

            return nRet == 1;
        }

        private bool GetGpioStatus(int nCamareIndex, int nPinIndex, ref bool bLevel)
        {
            if (nCamareIndex == -1) return false;
            if (nPinIndex >= 8 || nPinIndex < 0) return false;

            byte btStatus = 0;
            int nRet = KSJApiIo.KSJ_GpioGetStatus(nCamareIndex, ref btStatus);
            if (nRet != 1) return false;

            byte btMask = (byte)(0x01 << nPinIndex);

            bLevel = ((((btStatus & btMask) >> nPinIndex) == 0x00) ? false : true);

            return true;
        }
        #endregion


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
                m_results = alg.Run(m_frame.data, m_frame.iWidth, m_frame.iHeight);
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
                    frame.iWidth, frame.iHeight));
            }
        }

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
                    switch (e.type)
                    {
                        case GrabQueue.PACK_BEGIN_EVENT:
                            {
                                GWCameraLog.Instance.CameraRecogLog(string.Format("Package Begin: {0}.", e.packID));
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
                                        (ThreadedTaskBarcode t) =>
                                        {
                                            if (OutputMode == "0")
                                            {
                                                var results = t.GetResults();
                                                if (results != null)
                                                {
                                                    foreach (var result in results)
                                                    {
                                                        string barCode = System.Text.Encoding.Default.GetString(result.strCodeData).TrimEnd('\0');
                                                        GWCameraLog.Instance.CameraRecogLog(string.Format("KSJCameraHandler----->Camera {1} recognize barcode:{0}.",
                                                    barCode, m_DeviceInfo[t.GetFrame().hCamera].nSerials));
                                                        string[] str = new string[] { barCode };
                                                        if (GetImgInfoEvent != null) GetImgInfoEvent(str, null, 0);
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
                                GWCameraLog.Instance.CameraRecogLog(string.Format("KSJCameraHandler----->Package End: {0}.", e.packID));
                                GWCameraLog.Instance.CameraRecogLog(string.Format("PackID:{0} Wait TaskProcessor Stop.", e.packID));
                                TaskProcessor.Stop();
                                TaskProcessor = null;
                                GWCameraLog.Instance.CameraRecogLog("TaskProcessor Stopped.");
                                int nFrameProcessed = 0;
                                List<string> BarCode = new List<string>();
                                for (int i = 0; i < TaskList.Count; ++i)
                                {
                                    ThreadedTaskBarcode task = TaskList[i];
                                    if (task.IsCompleted())
                                    {
                                        ++nFrameProcessed;
                                        var Results = task.GetResults();
                                        if (Results != null)
                                        {
                                            foreach (var result in Results)
                                            {
                                                string barCode = System.Text.Encoding.Default.GetString(result.strCodeData).TrimEnd('\0');
                                                GWCameraLog.Instance.CameraRecogLog(string.Format("KSJCameraHandler----->Camera {1} recognize barcode:{0}.",
                                                    barCode, m_DeviceInfo[task.GetFrame().hCamera].nSerials));
                                                if (!BarCode.Contains(barCode))
                                                {
                                                    BarCode.Add(barCode);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // 这张图没有扫到码
                                        }
                                    }
                                    else
                                    {
                                        // 这张图没有运行算法
                                    }

                                    GWCameraLog.Instance.CameraRecogLog(m_DeviceInfo[task.GetFrame().hCamera].nSerials + " alg time: " + task.GetUseTime().ToString());
                                }


                                GWCameraLog.Instance.CameraRecogLog("end task!");

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
                                            GWCameraLog.Instance.CameraRecogLog("KSJCameraHandler----->this is a multi-barcode " + i.ToString() + " recognized barcode: " + str[i].ToString());
                                        }
                                        System.TimeSpan st = DateTime.Now.Subtract(startTime);
                                        System.TimeSpan st0 = DateTime.Now.Subtract(endTime);
                                        double time = double.Parse(st.TotalMilliseconds.ToString());
                                        if (cameraStyle == "1")
                                        {
                                            if (GetImgInfoEvent != null) GetImgInfoEvent(AddLineCode(str), null, time);
                                        }
                                        if (cameraStyle == "2")
                                        {
                                            GWCameraHandler.Instance.UpdateBarcode(str, null, time, 0);
                                        }
                                        GWCameraLog.Instance.CameraRecogLog("KSJCameraHandler ----->" + time.ToString() + "++++ from PACK_END_EVENT to send barcode" + st0.TotalMilliseconds.ToString());
                                        GWCameraLog.Instance.CameraRecogLog("KSJCameraHandler----->this is a whole process ending!!!");
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

                                        if (cameraStyle == "1")
                                        {
                                            if (GetImgInfoEvent != null) GetImgInfoEvent(AddLineCode(str1), null, time);
                                        }
                                        if (cameraStyle == "2")
                                        {
                                            GWCameraHandler.Instance.UpdateBarcode(str1, null, time, 0);
                                        }
                                        if (isSaveNoreadPic == "1" && BarCode[0].Length < 5 && AddLineCode(str1)[0] == "noread")//开了保存未识别图像功能且线扫也不识别才进行保存（如果识别的不是一个正常的条码也进行保存）
                                        {
                                            SaveImages(e.packID, TaskList);
                                        }
                                        //if (GetImgInfoEvent != null) GetImgInfoEvent(str1, null, time);
                                        //GWCameraHandler.Instance.UpdateBarcode(str1, null, time,1);
                                        GWCameraLog.Instance.CameraRecogLog("KSJCameraHandler ----->" + time.ToString() + "++++ from PACK_END_EVENT to send barcode" + st0.TotalMilliseconds.ToString());
                                        GWCameraLog.Instance.CameraRecogLog("KSJCameraHandler----->this is a whole process ending!!!");
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


                                        if (cameraStyle == "1")
                                        {
                                            if (GetImgInfoEvent != null) GetImgInfoEvent(AddLineCode(str1), null, time);
                                        }
                                        if (cameraStyle == "2")
                                        {
                                            GWCameraHandler.Instance.UpdateBarcode(str1, null, time, 0);
                                        }

                                        if (isSaveNoreadPic == "1" && AddLineCode(str1)[0] == "noread")//开了保存未识别图像功能已经线扫也不识别才进行保存
                                        {
                                            SaveImages(e.packID, TaskList);
                                        }

                                        GWCameraLog.Instance.CameraRecogLog("KSJCameraHandler ----->" + time.ToString() + "++++ from PACK_END_EVENT to send barcode" + st0.TotalMilliseconds.ToString());
                                        GWCameraLog.Instance.CameraRecogLog("KSJCameraHandler----->this is a whole process ending!!!");
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
            }
        }

        void DeleteFile(int id)
        {
            Task.Factory.StartNew(() =>
            {
                GWCameraLog.Instance.CameraRecogLog(id.ToString() + "delete Directory......");
                Directory.Delete(noreadSavePicPath + "/" + id.ToString(), true);
            });
        }



        /// <summary>
        /// Convert Image to Byte[]
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        public byte[] ImageToBytes(Image image)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                image.Save(stream, ImageFormat.Bmp);
                byte[] data = new byte[stream.Length];
                stream.Seek(0, SeekOrigin.Begin);
                stream.Read(data, 0, Convert.ToInt32(stream.Length));
                return data;
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

        #region 二值化
        /*
        1位深度图像 颜色表数组255个元素 只有用前两个 0对应0  1对应255 
        1位深度图像每个像素占一位
        8位深度图像每个像素占一个字节  是1位的8倍
        */
        /// <summary>
        /// 将源灰度图像二值化，并转化为1位二值图像。
        /// </summary>
        /// <param name="bmp"> 源灰度图像。 </param>
        /// <returns> 1位二值图像。 </returns>
        public Bitmap GTo2Bit(Bitmap bmp)
        {
            if (bmp != null)
            {
                // 将源图像内存区域锁定
                Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly,
                        PixelFormat.Format8bppIndexed);

                // 获取图像参数
                int leng, offset_1bit = 0;
                int width = bmpData.Width;
                int height = bmpData.Height;
                int stride = bmpData.Stride;  // 扫描线的宽度,比实际图片要大
                int offset = stride - width;  // 显示宽度与扫描线宽度的间隙
                IntPtr ptr = bmpData.Scan0;   // 获取bmpData的内存起始位置的指针
                int scanBytesLength = stride * height;  // 用stride宽度，表示这是内存区域的大小
                if (width % 32 == 0)
                {
                    leng = width / 8;
                }
                else
                {
                    leng = width / 8 + (4 - (width / 8 % 4));
                    if (width % 8 != 0)
                    {
                        offset_1bit = leng - width / 8;
                    }
                    else
                    {
                        offset_1bit = leng - width / 8;
                    }
                }

                // 分别设置两个位置指针，指向源数组和目标数组
                int posScan = 0, posDst = 0;
                byte[] rgbValues = new byte[scanBytesLength];  // 为目标数组分配内存
                Marshal.Copy(ptr, rgbValues, 0, scanBytesLength);  // 将图像数据拷贝到rgbValues中
                // 分配二值数组
                byte[] grayValues = new byte[leng * height]; // 不含未用空间。
                // 计算二值数组
                int x, v, t = 0;
                for (int i = 0; i < height; i++)
                {
                    for (x = 0; x < width; x++)
                    {
                        v = rgbValues[posScan];
                        t = (t << 1) | (v > 100 ? 1 : 0);


                        if (x % 8 == 7)
                        {
                            grayValues[posDst] = (byte)t;
                            posDst++;
                            t = 0;
                        }
                        posScan++;
                    }

                    if ((x %= 8) != 7)
                    {
                        t <<= 8 - x;
                        grayValues[posDst] = (byte)t;
                    }
                    // 跳过图像数据每行未用空间的字节，length = stride - width * bytePerPixel
                    posScan += offset;
                    posDst += offset_1bit;
                }

                // 内存解锁
                Marshal.Copy(rgbValues, 0, ptr, scanBytesLength);
                bmp.UnlockBits(bmpData);  // 解锁内存区域

                // 构建1位二值位图
                Bitmap retBitmap = twoBit(grayValues, width, height);
                return retBitmap;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 用二值数组新建一个1位二值图像。
        /// </summary>
        /// <param name="rawValues"> 二值数组(length = width * height)。 </param>
        /// <param name="width"> 图像宽度。 </param>
        /// <param name="height"> 图像高度。 </param>
        /// <returns> 新建的1位二值位图。 </returns>
        private Bitmap twoBit(byte[] rawValues, int width, int height)
        {
            // 新建一个1位二值位图，并锁定内存区域操作
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format1bppIndexed);
            BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height),
                 ImageLockMode.WriteOnly, PixelFormat.Format1bppIndexed);

            // 计算图像参数
            int offset = bmpData.Stride - bmpData.Width / 8;        // 计算每行未用空间字节数
            IntPtr ptr = bmpData.Scan0;                         // 获取首地址
            int scanBytes = bmpData.Stride * bmpData.Height;    // 图像字节数 = 扫描字节数 * 高度
            byte[] grayValues = new byte[scanBytes];            // 为图像数据分配内存

            // 为图像数据赋值
            int posScan = 0;                        // rawValues和grayValues的索引
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < bmpData.Width / 8; j++)
                {
                    grayValues[posScan] = rawValues[posScan];
                    posScan++;
                }
                // 跳过图像数据每行未用空间的字节，length = stride - width * bytePerPixel
                posScan += offset;
            }

            // 内存解锁
            Marshal.Copy(grayValues, 0, ptr, scanBytes);
            bitmap.UnlockBits(bmpData);  // 解锁内存区域

            // 修改生成位图的索引表
            ColorPalette palette;
            // 获取一个Format8bppIndexed格式图像的Palette对象
            using (Bitmap bmp = new Bitmap(1, 1, PixelFormat.Format1bppIndexed))
            {
                palette = bmp.Palette;
            }
            for (int i = 0; i < 2; i = +254)
            {
                palette.Entries[i] = Color.FromArgb(i, i, i);
            }
            // 修改生成位图的索引表
            bitmap.Palette = palette;

            return bitmap;
        }
        #endregion

        public Bitmap BytesToImg(byte[] bytes, int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            Marshal.Copy(bytes, 0, bmpData.Scan0, bytes.Length);
            bmp.UnlockBits(bmpData);
            return bmp;
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

        /// <summary>
        /// 单号内容显示
        /// </summary>
        /// <param name="value"></param>
        public void UpdateBarCodeContent(string value, int time)
        {
            lineCode = value;
        }

        /// <summary>
        /// 添加线扫相机的数据
        /// </summary>
        /// <param name="barCode"></param>
        /// <returns></returns>
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
                GWCameraLog.Instance.ExceptionInfoLog("KSJCameraHandler----->AddLineCode" + ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// 关闭ksjcamera
        /// </summary>
        public void Close()
        {
            if (cameraStyle == "1")
            {
                if (m_nDeviceNum > 0)
                {
                    try
                    {
                        if (m_ProcessThread != null)
                        {
                            m_Quit = true;
                            m_ProcessThread.Join();
                            m_ProcessThread = null;
                        }


                        m_Grab.Stop();
                        for (int i = 0; i < m_nDeviceNum; i++)
                        {
                            KSJApiTriggerMode.KSJ_TriggerModeSet(i, KSJApiTriggerMode.KSJ_TRIGGERMODE.KSJ_TRIGGER_INTERNAL);//设置相机内触发                            
                            KSJApiIo.KSJ_GpioSetStatus(i, 0x00);//高电平到低电平关闭光源                    
                        }

                        KSJApiBase.KSJ_UnInit();

                        //if (IsAddQueue == "1")
                        {
                            m_SaveImageProcessor.Stop();
                        }
                        m_AlgResourceMan.Cleanup();
                        GWCameraLog.Instance.InfoLog("KSJApiBase.KSJ_UnInit success！");
                    }
                    catch (Exception ex)
                    {
                        GWCameraLog.Instance.ExceptionInfoLog("Close: " + ex.ToString());
                    }
                }
            }
        }
    }


    public class GrabQueue
    {
        private List<int> m_CamList = new List<int>();
        private bool m_Quit = false;
        private List<Thread> m_GrabThreadList = new List<Thread>();
        private Thread m_TriggerLevelCheckThread;

        /// <summary>
        /// 清除相机数据的延时时间
        /// </summary>
        string clearTime = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "delayTime");
        string serials = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "Serials");
        string isSavePic = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "savePic");
        string StopCaptureTime = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "StopCaptureTime");
        string savePic = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "recPicPath");
        string LampStyle = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "LampStyle");
        /// <summary>
        /// 相机个数
        /// </summary>
        int cameraNum = 0;
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
            public int id;
            public int hCamera;
            public byte[] data;
            public int iWidth;
            public int iHeight;
            public string name;
        }

        private Object m_PackLock = new Object();
        private int m_PackIndex = 0;
        private Pack m_CurrentPack;
        private List<Event> m_EventList = new List<Event>();
        private EventWaitHandle m_EventListNotify = new EventWaitHandle(false, EventResetMode.ManualReset);

        public DEVICEINFO[] m_DeviceInfo = new DEVICEINFO[m_nMaxDevice];
        public const int m_nMaxDevice = 64;
        public struct DEVICEINFO
        {
            public int nIndex;
            public KSJApiBase.KSJ_DEVICETYPE DeviceType;
            public int nSerials;
            public ushort wFirmwareVersion;
            public ushort wFpgaVersion;
        };
        int[] M_WIDTH = null;
        int[] M_HEIGHT = null;
        int[] nCaptureBitCount = null;
        //byte[][] n_cameraData;
        public GrabQueue(int[] CamList, int[] WIDTH, int[] HEIGHT, int[] CaptureBitCount)
        {
            try
            {
                M_WIDTH = WIDTH;
                M_HEIGHT = HEIGHT;

                cameraNum = CamList.Length;
                nCaptureBitCount = new int[cameraNum];
                foreach (var hCam in CamList) //serials：1-8
                {
                    m_CamList.Add(hCam);
                }
                nCaptureBitCount = CaptureBitCount;
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("GrabQueue----->" + ex.ToString());
            }
        }

        public bool Start()
        {
            if (m_TriggerLevelCheckThread != null)
                return false;
            int i = 0;
            m_Quit = false;
            foreach (var hCam in m_CamList)
            {
                GWCameraLog.Instance.InfoLog("Start:" + i++.ToString());
                var t = new Thread(GrabThread);
                t.Start(hCam);
                m_GrabThreadList.Add(t);
            }

            m_TriggerLevelCheckThread = new Thread(ReadTriggerLevelThread);
            m_TriggerLevelCheckThread.Start();
            return true;
        }

        public void Stop()
        {
            try
            {
                m_Quit = true;
                for (int i = 0; i < cameraNum; i++)
                {
                    KSJApiBase.KSJ_SendPktEnd(i);
                }
                if (m_TriggerLevelCheckThread != null)
                {
                    m_TriggerLevelCheckThread.Join();
                    m_TriggerLevelCheckThread = null;
                }
                m_GrabThreadList.Clear();
                GWCameraLog.Instance.InfoLog("m_TriggerLevelCheckThread is stop!!!");
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("Stop: " + ex.ToString());
            }
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
                        //exitEvent.Set();
                    }//
                }
                DateTime Current = DateTime.Now;
                if (Current >= EndTime)
                    break;
                if (!m_EventListNotify.WaitOne(EndTime - Current))
                    break;
            }
            return null;
        }

        string noreadSavePicPath = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "noRecPicPath");
        /// <summary>
        /// 采集图像的线程
        /// </summary>
        /// <param name="arg"></param>
        private void GrabThread(Object arg)
        {
            try
            {
                int hCamera = (int)arg;
                KSJApiBase.KSJ_DeviceGetInformationEx(hCamera, ref m_DeviceInfo[hCamera].DeviceType, ref m_DeviceInfo[hCamera].nSerials, ref m_DeviceInfo[hCamera].wFirmwareVersion, ref m_DeviceInfo[hCamera].wFpgaVersion);

                while (!m_Quit) //while (!m_Quit)
                {
                    byte[] n_cameraData = new byte[M_WIDTH[0] * M_HEIGHT[0]];
                    int nRet = KSJApiBase.KSJ_CaptureRawData(hCamera, n_cameraData);//得到相机的图像数据
                    if (nRet == 1)
                    {
                        // 放到队列中
                        lock (m_PackLock)
                        {
                            if (m_CurrentPack != null)//只会处理前100张图像（如果现场出现卡包了一直拍照未识别的情况下会占用很多资源）
                            {
                                var NewFrame = new Frame();
                                NewFrame.hCamera = hCamera;
                                NewFrame.data = n_cameraData;
                                m_CurrentPack.frameCount++;
                                NewFrame.id = m_CurrentPack.frameCount;
                                NewFrame.iWidth = M_WIDTH[hCamera];
                                NewFrame.iHeight = M_HEIGHT[hCamera];
                                NewFrame.name = m_DeviceInfo[hCamera].nSerials.ToString();
                                NewEvent(PACK_FRAME_EVENT, NewFrame, m_CurrentPack.id,DateTime.Now);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("GrabThread----->" + ex.ToString());
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
            NewEvent(PACK_BEGIN_EVENT, null, m_CurrentPack.id,DateTime.Now);
            Console.WriteLine(string.Format("Package New, id: {0}", m_CurrentPack.id));
            GWCameraLog.Instance.CameraRecogLog(string.Format("Package New, id: {0}", m_CurrentPack.id));
        }

        private void OnEndPack()
        {
            GWCameraLog.Instance.CameraRecogLog(string.Format("KsjCameraHandler ---->Package Complete, id: {0}, FrameCount: {1}.wait for a moment and clear Pack Frame!!!",
                m_CurrentPack.id, m_CurrentPack.frameCount));
            //ClearPackFrame(m_CurrentPack.id);
            NewEvent(PACK_END_EVENT, null, m_CurrentPack.id, DateTime.Now);
            m_CurrentPack = null;
        }

        /// <summary>
        /// 清空相机缓存
        /// </summary>
        /// <param name="PackID"></param>
        private void ClearPackFrame(int PackID)
        {
            m_EventList.RemoveAll(e => e.type == PACK_FRAME_EVENT && e.packID == PackID);
        }

        /// <summary>
        /// 读取电平信号的线程
        /// </summary>
        /// <param name="arg"></param>
        private void ReadTriggerLevelThread()
        {
            byte lastState = 170;
            bool bLavel = true;
            while (!m_Quit)
            {
                #region 遍历所有相机的io
                //byte[] state0 = new byte[cameraNum];
                //int data = 0;
                //for (int i = 0; i < cameraNum; i++)
                //{
                //    data = KSJApiIo.KSJ_GpioGetStatus(i, ref state0[i]);
                //}
                //byte state = 0;
                //for (int i = 0; i < cameraNum; i++)
                //{
                //    if (state0[i] == 0)
                //    {
                //        state = 0;
                //    }
                //}
                //for (int i = 0; i < cameraNum; i++)
                //{
                //    if (state0[i] == 1)
                //    {
                //        state = 1;
                //    }
                //}
                //GWCameraLog.Instance.CameraRecogLog("KsjCameraHandler ---->" + state.ToString());
                #endregion
                byte state = 0;
                //int data = KSJApiIo.KSJ_GpioGetStatus(1, ref state);

                bool bRet = GetGpioStatus(0, 0, ref bLavel);
                state = (byte)(bLavel == true ? 1 : 0);
                //GWCameraLog.Instance.InfoLog("bRet:" + bRet.ToString() + "+++++" + "bLavel:" + bLavel.ToString());
                if (bRet == true)
                {
                    if (lastState != 170 && lastState != state)
                    {
                        // 电平变化
                        if (lastState != 1)
                        {
                            if (LampStyle == "0")
                            {
                                #region 高低电平控制光源长亮
                                for (int i = 0; i < m_CamList.Count; i++)
                                {
                                    SetGpioStatus(i, 1, true);
                                }
                                #endregion
                            }
                            GWCameraLog.Instance.CameraRecogLog("New package start : 1 -> 0");
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
                            GWCameraLog.Instance.CameraRecogLog("KsjCameraHandler ---->the package leave!!!");
                            if (LampStyle == "0")
                            {
                                Thread.Sleep(int.Parse(StopCaptureTime));//延迟一段时间将图像缓存清空
                                #region 高低电平控制光源长亮
                                for (int i = 0; i < m_CamList.Count; i++)
                                {
                                    SetGpioStatus(i, 1, false);
                                }
                                #endregion
                            }
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

        private bool SetGpioStatus(int nCamareIndex, int nPinIndex, bool bLevel)
        {
            if (nCamareIndex == -1) return false;
            if (nPinIndex >= 8 || nPinIndex < 0) return false;

            byte btStatus = 0;
            int nRet = KSJApiIo.KSJ_GpioGetStatus(nCamareIndex, ref btStatus);
            if (nRet != 1) return false;

            byte btMask = (byte)(0x01 << nPinIndex);

            if (bLevel) btStatus |= btMask;
            else btStatus &= (byte)(~btMask);

            nRet = KSJApiIo.KSJ_GpioSetStatus(nCamareIndex, btStatus);

            return nRet == 1;
        }

        private bool GetGpioStatus(int nCamareIndex, int nPinIndex, ref bool bLevel)
        {
            if (nCamareIndex == -1) return false;
            if (nPinIndex >= 8 || nPinIndex < 0) return false;

            byte btStatus = 0;
            int nRet = KSJApiIo.KSJ_GpioGetStatus(nCamareIndex, ref btStatus);
            if (nRet != 1) return false;

            byte btMask = (byte)(0x01 << nPinIndex);

            bLevel = ((((btStatus & btMask) >> nPinIndex) == 0x00) ? false : true);

            return true;
        }
    }
}

