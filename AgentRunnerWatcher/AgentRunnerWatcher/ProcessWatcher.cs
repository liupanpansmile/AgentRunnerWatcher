using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.IO;
using System.Configuration;
using System.Threading;

namespace AgentRunnerWatcher
{
    public partial class ProcessWatcher : ServiceBase
    {
        
        private string processAddress;              //进程地址
        private string processParameter;            //进程运行参数
        private string processWorkDirectory;        //进程工作目录
        private object lockerForLog = new object();
        private string logPath = string.Empty;
        public static int processID;                //工作进程ID
        
        private Thread watchThread;
        private delegate void KillThreadDelegate(Thread watchThread); 

        private KillThreadDelegate killThreadDelegate = null;
      
        public ProcessWatcher()
        {
            InitializeComponent();

            try
            {
                //读取监控进程全路径
                processAddress =  ConfigurationManager.AppSettings["ProcessAddress"].ToString();
                processParameter = ConfigurationManager.AppSettings["ProcessParameter"].ToString();
                processWorkDirectory = ConfigurationManager.AppSettings["ProcessWorkDirectory"].ToString();

                killThreadDelegate = new KillThreadDelegate(KillThread);

                if (processAddress == null)
                {
                    throw new Exception("读取配置档ProcessAddress失败，ProcessAddress为空！");
                }

                //创建日志目录
                this.logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AgentRunnerWatcherLog");
                if (!Directory.Exists(logPath))
                {
                    Directory.CreateDirectory(logPath);
                }
            }
            catch (Exception ex)
            {
                this.SaveLog("Watcher()初始化出错！错误描述为：" + ex.Message.ToString());
            }
        }


        // 启动服务
        protected override void OnStart(string[] args)
        {
            try
            {
               
                this.StartWatch();
            }
            catch (Exception ex)
            {
                this.SaveLog("OnStart() 出错，错误描述：" + ex.Message.ToString());
            }
        }

        // 停止服务
        protected override void OnStop()
        {
            try
            {
                StopWatch();
            }
            catch (Exception ex)
            {
                this.SaveLog("OnStop 出错，错误描述：" + ex.Message.ToString());
            }
        }



        // 开始监控
        private void StartWatch()
        {
            if (this.processAddress != null && this.processAddress.Length > 0 /*&& File.Exists(this.processAddress)*/)
            {
                this.ScanProcessList(this.processAddress, this.processParameter, this.processWorkDirectory);
            }
        }

        private void StopWatch()
        {
            Process process = Process.GetProcessById(processID);
            killThreadDelegate.Invoke(watchThread); //先杀死监控线程，然后再杀死工作进程
            if (process != null)
            {
                process.Kill();
            }
        }

        private void KillThread(Thread watchThread)
        {
            if (watchThread != null)
            {
                watchThread.Abort();
            }
        }
  
        private void ScanProcessList(string address, string parameter, string workDirectory)
        {
            Process[] arrayProcess = Process.GetProcesses();
            foreach (Process p in arrayProcess)
            {
                //System、Idle进程会拒绝访问其全路径
                if (p.ProcessName != "System" && p.ProcessName != "Idle")
                {
                    try
                    {
                      
                        if (this.FormatPath(address) == this.FormatPath(p.MainModule.FileName.ToString()))
                        {
                            //进程已启动
                            processID = p.Id;
                            this.WatchProcess(p, address, parameter, workDirectory);
                            this.SaveLog(address + " 已经启动");
                            return;
                        }
                    }
                    catch
                    {
                        //拒绝访问进程的全路径
                        this.SaveLog("进程(" + p.Id.ToString() + ")(" + p.ProcessName.ToString() + ")拒绝访问全路径！");
                    }
                }
            }
            this.SaveLog(address + "好美启动"); 
            //进程尚未启动
            Process process = new Process();
            process.StartInfo.FileName = address;
            process.StartInfo.Arguments = parameter;
            process.StartInfo.WorkingDirectory = workDirectory;
            process.StartInfo.UseShellExecute = false;
           
            process.Start();
            processID = process.Id;
           
            this.WatchProcess(process, address, parameter, workDirectory);
        }


       
        // 监听进程
        private void WatchProcess(Process process, string address, string parameter, string workDirectory)
        {
            ProcessRestart objProcessRestart = new ProcessRestart(process, address, parameter, workDirectory);
            Thread thread = new Thread(new ThreadStart(objProcessRestart.RestartProcess));
            watchThread = thread; //保存监视工作进程的线程
            thread.Start();
           
            
        }

        // 格式化路径 去除前后空格, 去除最后的"\", 字母全部转化为小写
        private string FormatPath(string path)
        {
            return path.ToLower().Trim().TrimEnd('\\');
        }


        // 记录日志
        public void SaveLog(string content)
        {
            try
            {
                lock (lockerForLog)
                {
                    FileStream fs;
                    fs = new FileStream(Path.Combine(this.logPath, DateTime.Now.ToString("yyyyMMdd") + ".log"), FileMode.OpenOrCreate);
                    StreamWriter streamWriter = new StreamWriter(fs);
                    streamWriter.BaseStream.Seek(0, SeekOrigin.End);
                    streamWriter.WriteLine("[" + DateTime.Now.ToString() + "]：" + content);
                    streamWriter.Flush();
                    streamWriter.Close();
                    fs.Close();
                }
            }
            catch
            {
            }
        }

    }


    public class ProcessRestart
    {
        //字段
        private Process process;
        private string address;
        private string parameter;
        private string workDirectory;

        public ProcessRestart()
        { }

        public ProcessRestart(Process process, string address, string parameter, string workDirectory)
        {
            this.process = process;
            this.address = address;
            this.parameter = parameter;
            this.workDirectory = workDirectory;

        }
  
        // 重启进程
        public void RestartProcess()
        {
            try
            {
                while (true)
                {
                   
                    this.process.WaitForExit();
                    this.process.Close();    //释放已退出进程的句柄
                    this.process.StartInfo.FileName = this.address;
                    this.process.StartInfo.Arguments = this.parameter;
                    this.process.StartInfo.WorkingDirectory = this.workDirectory;
                    this.process.StartInfo.UseShellExecute = false;
                    
                    ProcessWatcher objProcessWatcher = new ProcessWatcher();
                    
                    this.process.Start();
                    ProcessWatcher.processID = this.process.Id;
                    objProcessWatcher.SaveLog("process restarted  ,process id " + this.process.Id);
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                ProcessWatcher objProcessWatcher = new ProcessWatcher();
                objProcessWatcher.SaveLog("RestartProcess() 出错，监控程序已取消对进程("
                    + this.process.Id.ToString() + ")(" + this.process.ProcessName.ToString()
                    + ")的监控，错误描述为：" + ex.Message.ToString());
            }
        }


    }
}
