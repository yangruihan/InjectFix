using UnityEngine;
using UnityEditor;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System;

namespace IFix.Editor
{
    public class LiveDotNet : EditorWindow
    {
        private int platformIndex = 0;
        private string strIp = "";
        private int port = 8080;
        private string[] platforms = new string[] { "ios", "android" };

        [MenuItem("IFix/LiveDotNet")]
        private static void OpenWindow()
        {
            LiveDotNet window = GetWindow<LiveDotNet>();
            window.titleContent = new GUIContent("LiveDotNet");
        }

        void OnGUI()
        {
            platformIndex = EditorGUILayout.Popup("Platform: ", platformIndex, platforms);
            strIp = EditorGUILayout.TextField("IP: ", strIp);
            port = EditorGUILayout.IntField("Port: ", port);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("patch"))
                doPatch();
            // if (GUILayout.Button("inject"))
            //     inject();
            EditorGUILayout.EndHorizontal();
        }
        void inject()
        {
            
            IFixEditor.Platform platform = platformIndex == 0 ? IFixEditor.Platform.ios : IFixEditor.Platform.android;
            string f_dll;
            string s_dll;
            if (IFixEditor.BuildAssembly(platform, "ifix_inject", out f_dll, out s_dll))
            {
                //IFixEditor.InjectAssembly(f_dll);
                IFixEditor.InjectAssembly(s_dll);

                //File.Delete(f_dll);
                //File.Delete(f_dll + ".mdb");
                //File.Delete(s_dll);
                //File.Delete(s_dll + ".mdb");
            }
        }
        void doPatch()
        {
            IFixEditor.Platform platform = platformIndex == 0 ? IFixEditor.Platform.ios : IFixEditor.Platform.android;
            string patchPath = "Temp/tmp_live_dot_net_patch";

            string f_dll;
            string s_dll;
            if (IFixEditor.BuildAssembly(platform, "ifix_patch", out f_dll, out s_dll))
            {
                IFixEditor.GenPatch("Assembly-CSharp", s_dll, "./Assets/Plugins/IFix.Core.dll", patchPath);
                File.Delete(f_dll);
                File.Delete(f_dll + ".mdb");
                File.Delete(s_dll);
                File.Delete(s_dll + ".mdb");
            }

            IPAddress ip;
            if (!IPAddress.TryParse(strIp, out ip))
            {
                throw new FormatException("Invalid ip-adress");
            }

            IPEndPoint remoteEndPoint = new IPEndPoint(ip, port);
            doSend(File.ReadAllBytes(patchPath), remoteEndPoint);
            File.Delete(patchPath);
        }

        void doSend(byte[] bytes, IPEndPoint remoteEndPoint)
        {
            try
            {
                Socket sender = new Socket(remoteEndPoint.Address.AddressFamily,
                    SocketType.Stream, ProtocolType.Tcp);

                try
                {
                    IAsyncResult result = sender.BeginConnect(remoteEndPoint,null,null);

                    bool success = result.AsyncWaitHandle.WaitOne(1000, true);

                    if(sender.Connected)
                    {
                        sender.EndConnect(result);
                    }else
                    {
                        sender.Close();
                        throw new TimeoutException("Failed to connect " + remoteEndPoint);
                    }

                    Debug.Log(string.Format("Socket connected to {0}", sender.RemoteEndPoint.ToString()));

                    int bytesSent = sender.Send(bytes);

                    Debug.Log(string.Format("bytesSent = {0}", bytesSent));

                    sender.Shutdown(SocketShutdown.Both);
                    sender.Close();

                }
                catch (ArgumentNullException ane)
                {
                    Debug.LogError(string.Format("ArgumentNullException : {0}", ane.ToString()));
                }
                catch (SocketException se)
                {
                    Debug.LogError(string.Format("SocketException : {0}", se.ToString()));
                }
                catch (Exception e)
                {
                    Debug.LogError(string.Format("Unexpected exception : {0}", e.ToString()));
                }

            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }
    }
}
