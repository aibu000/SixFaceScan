using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GWCamera
{
    public class GWCameraLog
    {
        #region 单例模式
        private static readonly GWCameraLog instance = null;
        static GWCameraLog()
        {
            instance = new GWCameraLog();
        }
        private GWCameraLog()
        {
        }
        /// <summary>
        /// 单例模式
        /// </summary>
        public static GWCameraLog Instance
        {
            get
            {
                return instance;
            }
        }
        #endregion

        string logpath = System.Environment.CurrentDirectory; 
      
        #region method
        /// <summary>
        /// 记录日志的方法
        /// </summary>
        /// <param name="logFile"></param>
        /// <param name="info"></param>
        public void writelog(string logFile, string info)
        {
            FileStream fs = null;
            string msg = string.Empty;
            try
            {
                string date = DateTime.Now.ToString("yyyyMMdd");
                CheckLogFileExist(date);
                //if (info != string.Empty) msg = "【" + DateTime.Now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss fff") + "】" + info + "\r\n";
                if (info != string.Empty) msg = DateTime.Now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss fff") + "    " + info + "\r\n";
                else msg = info + "\r\n";
                byte[] myByte = System.Text.Encoding.UTF8.GetBytes(msg);//字符串转换为数组
                fs = new FileStream(logpath + "\\GWCameraLog\\" + date + "\\" + logFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                fs.Seek(0, SeekOrigin.End);//文件定位在末尾搜索
                fs.Write(myByte, 0, myByte.Length);
                fs.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine("writelog " + logFile + "操作失败:" + ex.ToString());
            }
            finally
            {
                if (fs != null)
                {
                    fs.Close();
                    fs.Dispose();
                }
            }
        }


        /// <summary>
        /// 检查文件夹是否存在
        /// </summary>
        public void CheckLogFileExist(string date)
        {
            if (!Directory.Exists(logpath + "\\GWCameraLog"))
                Directory.CreateDirectory(logpath + "\\GWCameraLog");
            if (!Directory.Exists(logpath + "\\GWCameraLog\\" + date))
                Directory.CreateDirectory(logpath + "\\GWCameraLog\\" + date);
        }

        /// <summary>
        /// 信息日志
        /// </summary>
        /// <param name="info"></param>
        public void InfoLog(string info)
        {
            writelog("CameraInfo" + ".txt", info);
        }

        public void ExceptionInfoLog(string info)
        {
            writelog("CameraException" + ".txt", info);
        }

        public void CameraRecogLog(string info)
        {
            writelog("CameraRecognize" +".txt", info);
        }
        #endregion
    }
}
