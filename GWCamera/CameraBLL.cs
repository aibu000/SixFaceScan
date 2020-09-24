using KSJSixScans;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GWCamera
{
    public class CameraBLL
    {
        #region 单例模式
        private static readonly CameraBLL instance = null;
        static CameraBLL()
        {
            instance = new CameraBLL();
        }
        private CameraBLL()
        {

        }
        /// <summary>
        /// 单例模式
        /// </summary>
        public static CameraBLL Instance
        {
            get
            {
                return instance;
            }
        }
        #endregion

        
        public delegate void GetAllCameraStateEventHandler(bool state);
        /// <summary>
        /// 获取相机状态
        /// </summary>
        public event GetAllCameraStateEventHandler GetAllCameraStateEvent;

        #region 获取到条码信息
        public delegate void GetAllBarCodeEventHandler(string value, byte[] img ,double time);
        /// <summary>
        /// 获取到有效数据
        /// </summary>
        /// <param name="state"></param>
        public event GetAllBarCodeEventHandler GetAllBarCodeEvent;
        #endregion
        /// <summary>
        /// 线扫相机类型 0-需要 1-不需要
        /// </summary>
        string LineCameraStyle = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "LineCameraStyle");
        /// <summary>
        /// 相机类型 0是mdws 1是ksj 2是组合相机
        /// </summary>
        string cameraStyle = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "CameraStyle");
        string noreadSavePicPath = XmlHelper.Instance.GetXMLInformation("/Config/cameraParam/" + "noRecPicPath");
        /// <summary>
        /// 初始化相机
        /// </summary>
        public void InitCamera()
        {
            GWCameraLog.Instance.InfoLog("****************************Version********************");
            GWCameraLog.Instance.InfoLog("2019-5-11 03:47：增加了固定相机曝光行数以及增益大小； 对应版本1.0.6.3");
            GWCameraLog.Instance.InfoLog("2019-5-12 23:12：1.MDWSCameraHandler保存图片的时候加锁；2.KSJCameraHandler初始化的时候不调用KSJ_UnInit；3.在打开光源之后清空相机缓存数据；对应版本1.0.6.4");
            GWCameraLog.Instance.InfoLog("2019-5-13 12:06：1.初始化相机的时候就把光源长亮，高低电平的时候不做处理；对应版本1.0.6.5");
            GWCameraLog.Instance.InfoLog("2019-5-13 15:40：1.电平信号变成低电平的时候发送KSJ_SendPktEnd命令，退出capture；对应版本1.0.6.6");
            GWCameraLog.Instance.InfoLog("2019-5-13 19:21：1.输出条码的时候判断前一个包裹 是否有相同条码；对应版本1.0.6.7");
            GWCameraLog.Instance.InfoLog("2019-5-14 23:35：1.记录日志的时候都用serials来记录；对应版本1.0.6.8");
            GWCameraLog.Instance.InfoLog("2019-5-15 16:13：1.保存图像在采集图像那块处理；2.光源改成高低电平控制长亮；对应版本1.0.6.9");
            GWCameraLog.Instance.InfoLog("2019-5-16 14:22：1.固定顶相机去读io变化；2.设置2000w相机高低电平触发；对应版本1.0.6.11");
            GWCameraLog.Instance.InfoLog("2019-5-16 23:05：1.去掉过滤功能；对应版本1.0.6.12");
            GWCameraLog.Instance.InfoLog("2019-5-29 17:44：1.狂扫，唯一模式都可以保存未识别图像；对应版本1.0.6.15");
            GWCameraLog.Instance.InfoLog("2019-06-01 17:44：1.使用任务保存未识别图像；对应版本1.0.6.16");
            GWCameraLog.Instance.InfoLog("2019-06-02 16:13：1.添加了选择ksj日志存放位置功能；2.改成只读一个相机的电平信号；对应版本1.0.6.17");
            GWCameraLog.Instance.InfoLog("2019-06-05 01:15：1.新的接口采用1.1.6.*模式；2.凯视佳和迈德威视都可以通过多核模式保存未识别图像；对应版本1.1.6.1");
            GWCameraLog.Instance.InfoLog("2019-06-05 01:15：1.各种相机上车区模式（即唯一模式）传出来的时间改成包裹碰到光电的时间；对应版本1.1.6.2");
            GWCameraLog.Instance.InfoLog("2019-06-19 11:15：1.保存的图像根据配置文件中的isaddqueue是否添加至task；对应版本1.1.6.3");
            GWCameraLog.Instance.InfoLog("2019-06-24 10:17：1.回调传图；对应版本1.1.6.4");
            GWCameraLog.Instance.InfoLog("2019-06-25 09:17：1.回调传图,将灰度图转换成bitmap再传出去；对应版本1.1.6.5");
            GWCameraLog.Instance.InfoLog("2019-06-26 15:07：1.修复了上一版本传图有问题的bug；对应版本1.1.6.6");
            GWCameraLog.Instance.InfoLog("2019-06-27 11:22：1.mdwscamera狂扫模式跟唯一模式都将灰度图转成rgb传出去；对应版本1.1.6.7");
            GWCameraLog.Instance.InfoLog("2019-06-27 22:46：1.各个mdws、ksj相机打印每一张图像经过算法的时间+这个图像对应的相机名字；对应版本1.1.6.8");
            GWCameraLog.Instance.InfoLog("2019-06-27 23:10：1.MDWSCamera狂扫模式将rgb等倍缩放之后再传出；对应版本1.1.6.9");
            GWCameraLog.Instance.InfoLog("2019-06-28 23:10：1.MDWSCamera狂扫模式将rgb等倍缩放之后再传出----->取消；对应版本1.1.6.10");
            GWCameraLog.Instance.InfoLog("2019-07-01 00:10：1.将AsyncTCPClient类改成ZKGWAsyncTCPClient；对应版本1.1.6.11");
            GWCameraLog.Instance.InfoLog("2019-07-01 21:29：1.mdws回调的时候将之前的bitmap转byte数组先屏蔽掉；对应版本1.1.6.12");
            GWCameraLog.Instance.InfoLog("2019-07-02 22:59：1.删除7天之外的未识别图像；对应版本1.1.6.13");
            GWCameraLog.Instance.InfoLog("2019-07-05 11:12：1.判断是否有D:\\GWCameraPic\\noread\\这个路径被拒绝，将路径改成D:\\GWCameraPic\\noread；2.增加一系列的日志方便查看问题；对应版本1.1.6.14");
            GWCameraLog.Instance.InfoLog("2019-07-06 00:04：1.增加了几句日志打印便于分析Package Begin: 这句话跟New package start : 1 -> 0这句话时间有差异 对应版本1.1.6.15");
            GWCameraLog.Instance.InfoLog("2019-07-06 00:25：1.固定mdws相机帧率配合u3相机升级相机固件以及sdk； 对应版本1.1.6.16");
            GWCameraLog.Instance.InfoLog("*******************************************************");


            try
            {
                #region 重命名noread文件夹
                noreadSavePicPath = noreadSavePicPath.Substring(0, noreadSavePicPath.Length - 1);
                if (Directory.Exists(noreadSavePicPath))
                {
                    string newPath = string.Format(noreadSavePicPath.Replace("noread", "") + "noread" + DateTime.Now.ToString("yyyyMMddhhmmss"));
                    Directory.Move(noreadSavePicPath, newPath);
                }
                #endregion

                #region 重命名ksjlog文件夹
                //if (Directory.Exists(noreadSavePicPath))
                //{
                //    string newPath = string.Format(noreadSavePicPath.Replace("noread", "") + "noread" + DateTime.Now.ToString("yyyyMMddhhmmss"));
                //    Directory.Move(noreadSavePicPath, newPath);
                //}
                #endregion

                #region 删除未识别图像
                string path = string.Format(@"D:\GWCameraPic\");
                DelectDir(path);
                #endregion

                if (cameraStyle == "0" || cameraStyle == "2")
                {
                    MDWSCameraHandler.Instance.GetCameraStateEvent += new MDWSCameraHandler.GetCameraStateEventHandler(ShowCameraState);
                    MDWSCameraHandler.Instance.GetImgInfoEvent += Instance_GetImgInfoEvent;
                    MDWSCameraHandler.Instance.init();
                }
                if (cameraStyle == "1" || cameraStyle == "2")
                {
                    KsjCameraHandler.Instance.GetImgInfoEvent += Instance_GetImgInfoEvent;
                    KsjCameraHandler.Instance.GetCameraStateEvent += new KsjCameraHandler.GetCameraStateEventHandler(ShowCameraState);
                    KsjCameraHandler.Instance.InitCamera();
                }
                if (LineCameraStyle == "0")
                {
                    //ZKGWLineCameraHandler.Instance.GetLineCameraInfoEvent += new ZKGWLineCameraHandler.GetLineCameraInfoEventHandler(ShowInfo);
                    ZKGWLineCameraHandler.Instance.GetBarCodeEvent += UpdateBarCodeContent;
                    ZKGWLineCameraHandler.UpdateNetStateEvent += UpdateCameraState;
                    ZKGWLineCameraHandler.Instance.Connect();
                }
                if (cameraStyle == "2")
                {
                    GWCameraHandler.Instance.GetBarCodeInfoEvent += new GWCameraHandler.GetBarCodeInfoEventHandler(Instance_GetImgInfoEvent);
                }
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("InitCamera: " + ex.ToString());
            }
        }

        /// <summary>
        /// 删除7天之外的未识别图像
        /// </summary>
        /// <param name="srcPath"></param>
        public static void DelectDir(string srcPath)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(srcPath);
                FileSystemInfo[] fileinfo = dir.GetFileSystemInfos();  //返回目录中所有文件和子目录
                foreach (FileSystemInfo i in fileinfo)
                {
                    if (i is DirectoryInfo)            //判断是否文件夹
                    {
                        if (i.ToString().Length == 16)
                        {
                            string fileName = i.ToString().Substring((i.ToString().IndexOf('d') + 1), 4);
                            DateTime dt = DateTime.ParseExact(fileName, "MMdd", Thread.CurrentThread.CurrentCulture); //Convert.ToDateTime("2019" + fileName);
                            if (DateTime.Now.Subtract(dt).TotalDays > 7)
                            {
                                DirectoryInfo subdir = new DirectoryInfo(i.FullName);
                                subdir.Delete(true);          //删除子目录和文件
                            }
                        }
                    }
                    else
                    {
                        //如果 使用了 streamreader 在删除前 必须先关闭流 ，否则无法删除 sr.close();
                        File.Delete(i.FullName);      //删除指定文件
                    }
                }
            }
            catch (Exception e)
            {
                throw;
            }
        }


        string lineCode = "";
        string[] ksjCameraCode;
        public void Instance_GetImgInfoEvent(string[] barCode, byte[] image, double time)
        {
            try
            {
                List<string> list = barCode.ToList();
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

                string code = "";
                for (int p = 0; p < barCode.Length; p++)
                {
                    if (p == barCode.Length - 1)
                    {
                        code += barCode[p];
                    }
                    else
                    {
                        code += barCode[p] + "+";
                    }
                }
                if (GetAllBarCodeEvent != null) GetAllBarCodeEvent(code, image, time);
                GWCameraLog.Instance.CameraRecogLog("six cameras recognize:" + code);
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("Instance_GetImgInfoEvent------>" + ex.ToString());
            }
        }

        /// <summary>
        /// ksj和mdws的相机状态
        /// </summary>
        /// <param name="state"></param>
        public void ShowCameraState(bool state)
        {
            if (GetAllCameraStateEvent != null) GetAllCameraStateEvent(state);
        }

        /// <summary>
        /// 线扫相机的状态
        /// </summary>
        /// <param name="state"></param>
        public void UpdateCameraState(bool? state)
        {
            if (GetAllCameraStateEvent != null) GetAllCameraStateEvent((bool)state);
        }

        /// <summary>
        /// 获取线扫相机的数据
        /// </summary>
        /// <param name="value"></param>
        /// <param name="time"></param>
        public void UpdateBarCodeContent(string value,int time)
        {
            lineCode = value;
        }

        /// <summary>
        /// 释放相机资源
        /// </summary>
        public void CloseCamera()
        {
            try
            {
                GWCameraLog.Instance.InfoLog("Begin to CloseCamera!!!");
                GWCameraHandler.Instance.Close();
                MDWSCameraHandler.Instance.Close();
                ZKGWLineCameraHandler.Instance.Close();
                KsjCameraHandler.Instance.Close();
                GWCameraLog.Instance.InfoLog("CloseCamera success!!!");
            }
            catch (Exception ex) 
            {
                GWCameraLog.Instance.ExceptionInfoLog(string.Format("{0}:{1}", "CameraBLL->CloseCamera", ex.ToString()));
            }
        }
    }
}
