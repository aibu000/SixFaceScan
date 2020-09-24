using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace GWCamera
{
    /// <summary>
    /// XML配置文件操作类
    /// </summary>
    public class XmlHelper
    {
        #region object
        public static XmlDocument xmlDoc; //读取xml配置文件
        #endregion

        #region variables
        //public static string xmlPath = System.IO.Directory.GetCurrentDirectory() + @"\Config.xml";
        public string xmlPath = "ZKGWConfig.xml";
        #endregion

        #region 单例模式
        private static readonly XmlHelper instance = null;
        static XmlHelper()
        {
            instance = new XmlHelper();
        }
        private XmlHelper()
        {
        }
        public static XmlHelper Instance
        {
            get
            {
                return instance;
            }
        }
        #endregion

        /// <summary>
        /// 获取XML节点信息
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        public string GetXMLInformation(string arg)
        {
            string value = "";
            try
            {
                xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlPath);
                XmlNode xnl = xmlDoc.SelectSingleNode(arg);
                value = xnl.InnerText;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            return value;
        }

        /// <summary>
        /// 从xml中获取参数
        /// </summary>
        /// <param name="file"></param>
        /// <param name="arg"></param>
        /// <returns></returns>
        public string GetXmlFromFile(string file, string arg)
        {
            string value = "";
            try
            {
                xmlDoc = new XmlDocument();
                xmlDoc.Load(file);
                XmlNode xnl = xmlDoc.SelectSingleNode(arg);
                value = xnl.InnerText;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            return value;
        }

        /// <summary>
        /// 修改XML节点信息
        /// </summary>
        /// <param name="node"></param>
        /// <param name="arg"></param>
        public void UpdateXMLInformation(string node, string arg)
        {
            try
            {
                xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlPath);
                XmlNode xnl = xmlDoc.SelectSingleNode(node);
                xnl.InnerText = arg;
                xmlDoc.Save(xmlPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
