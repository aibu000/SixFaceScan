using GWCamera;
using KSJSixScans;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SixFaceScanCode
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CameraBLL.Instance.GetAllBarCodeEvent += new CameraBLL.GetAllBarCodeEventHandler(Instance_GetImgInfoEvent);
            CameraBLL.Instance.GetAllCameraStateEvent += new CameraBLL.GetAllCameraStateEventHandler(ShowCameraState);
            CameraBLL.Instance.InitCamera();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            GWCameraLog.Instance.CameraRecogLog($"关闭程序");
            CameraBLL.Instance.GetAllBarCodeEvent -= new CameraBLL.GetAllBarCodeEventHandler(Instance_GetImgInfoEvent);
            CameraBLL.Instance.GetAllCameraStateEvent -= new CameraBLL.GetAllCameraStateEventHandler(ShowCameraState);
            CameraBLL.Instance.CloseCamera();
        }


        int i = 0;
        string lineCode = "";
        string[] ksjCameraCode;
        public void Instance_GetImgInfoEvent(string barCode, byte[] image, double time)
        {
            try
            {
                i++;
                this.Dispatcher.Invoke(new Action(() =>
                {
                    listview.Items.Add(new { num = i, barcode = barCode });
                    listview.SelectedIndex = listview.Items.Count - 1;
                    listview.ScrollIntoView(listview.SelectedItem);
                }));
            }
            catch (Exception ex)
            {
                GWCameraLog.Instance.ExceptionInfoLog("Instance_GetImgInfoEvent------>" + ex.ToString());
            }
        }

        public void ShowCameraState(bool state)
        {

        }

        private void btn_save_Click(object sender, RoutedEventArgs e)
        {
            
        }
    }
}
