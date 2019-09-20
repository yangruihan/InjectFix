/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using System.Reflection;
#if UNITY_2018_3_OR_NEWER
using UnityEditor.Build.Player;
#endif

namespace IFix.Editor
{
    //版本选择窗口
    public class VersionSelector : EditorWindow
    {
        public string buttonText = "Patch";
        public string[] options = new string[] {};
        public int index = 0;
        public Action<int> callback = null;

        public static void Show(string[] options, Action<int> callback, string buttonText = "Patch")
        {
            VersionSelector window = GetWindow<VersionSelector>();
            window.options = options;
            window.callback = callback;
            window.buttonText = buttonText;
            window.Show();
        }

        void OnGUI()
        {
            index = EditorGUILayout.Popup("Please select a version: ", index, options);
            if (GUILayout.Button(buttonText))
                doPatch();
        }

        void doPatch()
        {
            if (callback != null)
            {
                callback(index);
            }
            Close();
        }
    }

    public class IFixEditor
    {
        //备份目录
        const string BACKUP_PATH = "./IFixDllBackup";
        //备份文件的时间戳生成格式
        const string TIMESTAMP_FORMAT = "yyyyMMddHHmmss";

        //system("mono ifix.exe [args]")
        public static void CallIFix(List<string> args)
        {
#if UNITY_EDITOR_OSX
            var mono_path = Path.Combine(Path.GetDirectoryName(typeof(UnityEngine.Debug).Module.FullyQualifiedName),
                "../MonoBleedingEdge/bin/mono");
            if(!File.Exists(mono_path))
            {
                mono_path = Path.Combine(Path.GetDirectoryName(typeof(UnityEngine.Debug).Module.FullyQualifiedName),
                    "../../MonoBleedingEdge/bin/mono");
            }
#elif UNITY_EDITOR_WIN
            var mono_path = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName),
                "Data/MonoBleedingEdge/bin/mono.exe");
#endif
            if (!File.Exists(mono_path))
            {
                UnityEngine.Debug.LogError("can not find mono!");
            }
            var inject_tool_path = "./IFixToolKit/IFix.exe";
            //"--runtime = v4.0.30319"
            if (!File.Exists(inject_tool_path))
            {
                UnityEngine.Debug.LogError("please install the ToolKit");
                return;
            }

            Process hotfix_injection = new Process();
            hotfix_injection.StartInfo.FileName = mono_path;
#if UNITY_5_6_OR_NEWER
            hotfix_injection.StartInfo.Arguments = "--runtime=v4.0.30319 \"" + inject_tool_path + "\" \""
#else
            hotfix_injection.StartInfo.Arguments = "\"" + inject_tool_path + "\" \""
#endif
                + string.Join("\" \"", args.ToArray()) + "\"";
            hotfix_injection.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            hotfix_injection.StartInfo.RedirectStandardOutput = true;
            hotfix_injection.StartInfo.UseShellExecute = false;
            hotfix_injection.StartInfo.CreateNoWindow = true;
            hotfix_injection.Start();

            //UnityEngine.Debug.Log(hotfix_injection.StartInfo.FileName);
            //UnityEngine.Debug.Log(hotfix_injection.StartInfo.Arguments);

            StringBuilder exceptionInfo = null;
            while(!hotfix_injection.StandardOutput.EndOfStream)
            {
                string line = hotfix_injection.StandardOutput.ReadLine();
                if (exceptionInfo != null)
                {
                    exceptionInfo.AppendLine(line);
                }
                else
                {
                    if (line.StartsWith("Warning:"))
                    {
                        //UnityEngine.Debug.LogWarning(line);
                    }
                    else if (line.StartsWith("Error:"))
                    {
                        UnityEngine.Debug.LogError(line);
                    }
                    else if (line.StartsWith("Unhandled Exception:"))
                    {
                        exceptionInfo = new StringBuilder(line);
                    }
                    else
                    {
                        UnityEngine.Debug.Log(line);
                    }
                }
            }
            hotfix_injection.WaitForExit();
            if (exceptionInfo != null)
            {
                UnityEngine.Debug.LogError(exceptionInfo);
            }
        }

        [MenuItem("InjectFix/Inject", false, 1)]
        public static void InjectAssemblys()
        {
            if (EditorApplication.isCompiling || Application.isPlaying)
            {
                UnityEngine.Debug.LogError("compiling or playing");
                return;
            }
            AutoInject = true;
            InjectAllAssemblys();
        }

        public static bool AutoInject = false; //可以在外部禁用掉自动注入

        public static bool InjectOnce = false; //AutoInjectAssemblys只调用一次，可以防止自动化打包时，很多场景导致AutoInjectAssemblys被多次调用

        static bool injected = false;

        [UnityEditor.Callbacks.PostProcessScene(100)]
        public static void AutoInjectAssemblys()
        {
            if (AutoInject && !injected)
            {
                InjectAllAssemblys();
                if (InjectOnce)
                {
                    injected = true;
                }
            }
        }

        //获取备份文件信息
        public static void GetBackupInfo(out string[] backups, out string[] timestamps)
        {
            string pattern = @"Assembly-CSharp-(\d{14})\.dll$";
            Regex r = new Regex(pattern);

            var allBackup = Directory.GetFiles(BACKUP_PATH).Where(path => r.Match(path).Success)
                .Select(path => path.Replace('\\', '/')).ToList();
            allBackup.Sort();

            backups = allBackup.Select(path => r.Match(path).Groups[1].Captures[0].Value).ToArray();
            timestamps = allBackup.Select(path => DateTime.ParseExact(r.Match(path).Groups[1].Captures[0].Value,
                TIMESTAMP_FORMAT, System.Globalization.CultureInfo.InvariantCulture)
                .ToString("yyyy-MM-dd hh:mm:ss tt")).ToArray();
        }

        //选择备份
        public static void SelectBackup(string buttonText, Action<string> cb)
        {
            string[] backups;
            string[] timestamps;
            GetBackupInfo(out backups, out timestamps);

            VersionSelector.Show(timestamps.ToArray(), index =>
            {
                cb(backups[index]);
            }, buttonText);
        }

        /// <summary>
        /// 对指定的程序集注入
        /// </summary>
        /// <param name="assembly">程序集路径</param>
        public static void InjectAssembly(string assembly)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assembly);
            var configure = Configure.GetConfigureByTags(new List<string>() {
                "IFix.IFixAttribute",
                "IFix.InterpretAttribute",
                "IFix.ReverseWrapperAttribute",
            });

            var processCfgPath = "./process_cfg";

            //该程序集是否有配置了些类，如果没有就跳过注入操作
            bool hasSomethingToDo = false;

            using (BinaryWriter writer = new BinaryWriter(new FileStream(processCfgPath, FileMode.Create,
                FileAccess.Write)))
            {
                writer.Write(configure.Count);

                foreach (var kv in configure)
                {
                    writer.Write(kv.Key);

                    var typeList = kv.Value.Where(item => item.Key is Type)
                        .Select(item => new KeyValuePair<Type, int>(item.Key as Type, item.Value))
                        .Where(item => item.Key.Assembly.GetName().Name == assemblyName)
                        .ToList();
                    writer.Write(typeList.Count);

                    if (typeList.Count > 0)
                    {
                        hasSomethingToDo = true;
                    }

                    foreach (var cfgItem in typeList)
                    {
                        writer.Write(GetCecilTypeName(cfgItem.Key));
                        writer.Write(cfgItem.Value);
                    }
                }
            }

            if (hasSomethingToDo)
            {

                var core_path = "./Assets/Plugins/IFix.Core.dll";
                var patch_path = string.Format("./{0}.ill.bytes", assemblyName);
                List<string> args = new List<string>() { "-inject", core_path, assembly,
                    processCfgPath, patch_path, assembly };

                foreach (var path in
                    (from asm in AppDomain.CurrentDomain.GetAssemblies()
                        select Path.GetDirectoryName(asm.ManifestModule.FullyQualifiedName)).Distinct())
                {
                    try
                    {
                        //UnityEngine.Debug.Log("searchPath:" + path);
                        args.Add(path);
                    }
                    catch { }
                }
                GetAssetsDllPaths(args, "Assets");

                CallIFix(args);
            }

            File.Delete(processCfgPath);
        }
        public static void GetAssetsDllPaths(List<string> args, string dir)
        {

            foreach (var file in Directory.GetFiles(dir))
            {
                if (file.IndexOf(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar, StringComparison.Ordinal) > 0)
                {
                    return;
                }
                if (Path.GetExtension(file).ToLower() == ".dll")
                {
                    args.Add(Path.GetDirectoryName(file));
                    break;
                }
            }

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                GetAssetsDllPaths(args, subDir);
            }
        }

        /// <summary>
        /// 对injectAssemblys里的程序集进行注入，然后备份
        /// </summary>
        public static void InjectAllAssemblys()
        {
            if (EditorApplication.isCompiling || Application.isPlaying)
            {
                return;
            }

            foreach (var assembly in injectAssemblys)
            {
                InjectAssembly(assembly);
            }
            
            //doBackup(DateTime.Now.ToString(TIMESTAMP_FORMAT));

            AssetDatabase.Refresh();
        }

        //默认的注入及备份程序集
        //另外可以直接调用InjectAssembly对其它程序集进行注入。
        static string[] injectAssemblys = new string[]
        {
            "./Library/ScriptAssemblies/Assembly-CSharp.dll",
            "./Library/ScriptAssemblies/Assembly-CSharp-firstpass.dll",
            
        };

        public static void AddCustomAssmblyToInject(string dllPath)
        {
            if(!string.IsNullOrEmpty(dllPath) && !injectAssemblys.Contains(dllPath))
            {
                injectAssemblys = injectAssemblys.Concat(new string[] { dllPath }).ToArray();
            }
        }

        /// <summary>
        /// 把注入后的程序集备份
        /// </summary>
        /// <param name="ts">时间戳</param>
        static void doBackup(string ts)
        {
            if (!Directory.Exists(BACKUP_PATH))
            {
                Directory.CreateDirectory(BACKUP_PATH);
            }


            foreach (var assembly in injectAssemblys)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(assembly);
                if (!File.Exists(assembly))
                {
                    continue;
                }
                File.Copy(assembly, string.Format("{0}/{1}-{2}.dll", BACKUP_PATH, assemblyName, ts), true);

                var mdbFile = string.Format("{0}.mdb", assembly);
                if (File.Exists(mdbFile))
                {
                    File.Copy(mdbFile, string.Format("{0}/{1}-{2}.dll.mdb", BACKUP_PATH, assemblyName, ts), true);
                }

                var pdbFile = string.Format("{0}.pdb", assembly);
                if (File.Exists(pdbFile))
                {
                    File.Copy(pdbFile, string.Format("{0}/{1}-{2}.dll.pdb", BACKUP_PATH, assemblyName, ts), true);
                }
            }
        }

        /// <summary>
        /// 恢复某个选定的备份
        /// </summary>
        /// <param name="ts">时间戳</param>
        static void doRestore(string ts)
        {

            foreach (var assembly in injectAssemblys)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(assembly);
                var dllFile = string.Format("{0}/{1}-{2}.dll", BACKUP_PATH, assemblyName, ts);
                if (!File.Exists(dllFile))
                {
                    continue;
                }
                File.Copy(dllFile, assembly, true);
                UnityEngine.Debug.Log("Revert to: " + dllFile);

                var mdbFile = string.Format("{0}/{1}-{2}.dll.mdb", BACKUP_PATH, assemblyName, ts);
                if (File.Exists(mdbFile))
                {
                    File.Copy(mdbFile, string.Format("{0}.mdb", assembly), true);
                    UnityEngine.Debug.Log("Revert to: " + mdbFile);
                }

                var pdbFile = string.Format("{0}/{1}-{2}.dll.pdb", BACKUP_PATH, assemblyName, ts);
                if (File.Exists(pdbFile))
                {
                    File.Copy(pdbFile, string.Format("{0}.pdb", assembly), true);
                    UnityEngine.Debug.Log("Revert to: " + pdbFile);
                }
            }
        }

        //cecil里的类名表示和.net标准并不一样，这里做些转换
        static string GetCecilTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                if (type.IsGenericTypeDefinition)
                {
                    return type.ToString().Replace('+', '/').Replace('[', '<').Replace(']', '>');
                }
                else
                {
                    return Regex.Replace(type.ToString().Replace('+', '/'), @"(`\d).+", "$1")
                        + "<" + string.Join(",", type.GetGenericArguments().Select(t => GetCecilTypeName(t))
                        .ToArray()) + ">";
                }
            }
            else
            {
                return type.FullName.Replace('+', '/');
            }
        }

        //目前支持的平台编译
        public enum Platform
        {
            android,
            ios,
            standalone
        }

        //缓存：解析好的编译参数
        private static Dictionary<string, string> compileTemplates = new Dictionary<string, string>();

        

        //对路径预处理，然后添加到StringBuilder
        //规则：如果路径含空格，则加上双引号
        static void AppendFile(StringBuilder sb, string path)
        {
            if (path.IndexOf(' ') > 0)
            {
                sb.Append('"');
                sb.Append(path);
                sb.Append('"');
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine(path);
            }
        }

        //自动加入源码
        public static void AppendDirectory(StringBuilder src, string dir, bool isFirst=false)
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                if (file.IndexOf(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar, StringComparison.Ordinal) > 0)
                {
                    continue;
                }
                if (file.Substring(file.Length - 3).ToLower() == ".cs")
                {
                    if (file.StartsWith("Assets" + Path.DirectorySeparatorChar + "Plugins", StringComparison.Ordinal) ||
                        file.StartsWith("Assets" + Path.DirectorySeparatorChar + "Standard Assets", StringComparison.Ordinal) ||
                        file.StartsWith("Assets" + Path.DirectorySeparatorChar + "Pro Standard Assets", StringComparison.Ordinal))
                    {
                        if (isFirst) AppendFile(src, file);
                    }
                    else
                    {
                        if (!isFirst) AppendFile(src, file);
                    }

                }
            }

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                AppendDirectory(src, subDir, isFirst);
            }
        }

        //通过模板文件，获取编译参数
        private static string GetCompileArguments(string s_tmp_tpl, string s_src_tpl, string output, bool isFirst)
        {
            string compileTemplate;
            if (!File.Exists(s_src_tpl))
            {
                throw new InvalidOperationException("please put template file in IFixToolKit directory!");
            }
            if (!compileTemplates.TryGetValue(s_tmp_tpl, out compileTemplate))
            {
                compileTemplate = string.Join("\n", File.ReadAllLines(s_src_tpl).Where(line => !line.StartsWith("Assets/", StringComparison.Ordinal)
                && !line.StartsWith("\"Assets/", StringComparison.Ordinal)
                && !line.StartsWith("'Assets/", StringComparison.Ordinal)
                && !line.StartsWith("-out", StringComparison.Ordinal)
                && !line.StartsWith("-r:'Library/ScriptAssemblies/Assembly-CSharp-firstpass.dll'", StringComparison.Ordinal)
                ).ToArray());
                compileTemplates.Add(s_tmp_tpl, compileTemplate);
            }

            StringBuilder cmd = new StringBuilder();
            StringBuilder src = new StringBuilder();

            AppendDirectory(src, "Assets", isFirst);
            var projectDir = Application.dataPath.Replace(Path.DirectorySeparatorChar, '/');
//            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
//            {
//                try
//                {
//#if (UNITY_EDITOR || XLUA_GENERAL) && !NET_STANDARD_2_0
//                    if (!(assembly.ManifestModule is System.Reflection.Emit.ModuleBuilder))
//                    {
//#endif
//                        var assemblyPath = assembly.ManifestModule.FullyQualifiedName
//                            .Replace(Path.DirectorySeparatorChar, '/');
//                        if (assemblyPath.StartsWith(projectDir))
//                        {
//                            dll.Append("-r:");
//                            appendFile(dll, assemblyPath.Replace(projectDir, "Assets"));
//                        }
//#if (UNITY_EDITOR || XLUA_GENERAL) && !NET_STANDARD_2_0
//                    }
//#endif
//                }
//                catch { }
//            }

            cmd.AppendLine(compileTemplate);
            cmd.Append(src.ToString());
            cmd.Append("-out:");
            AppendFile(cmd, output);
#if UNITY_EDITOR_OSX
            var UnityEngineDir = Path.GetDirectoryName(typeof(UnityEngine.Debug).Module.FullyQualifiedName);

            var ContentDir = Path.GetDirectoryName(Path.GetDirectoryName(UnityEngineDir));
            var UnityPath = Path.GetDirectoryName(Path.GetDirectoryName(ContentDir));


#elif UNITY_EDITOR_WIN
            var ContentDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName).Replace(Path.DirectorySeparatorChar, '/');;
            var UnityPath = ContentDir;
#endif

            return cmd.ToString().Replace("${UnityHome}", UnityPath)
                .Replace("${UnityHome}/Unity.app/Contents", ContentDir);
        }
        //1、解析编译参数（处理元文件之外的编译参数）
        //2、搜索工程的c#源码，加上编译参数编译
        //3、编译Assembly-CSharp.dll
        //TODO: 只支持Assembly-CSharp.dll，但较新版本Unity已经支持多dll拆分
        //TODO: 目前的做法挺繁琐的，需要用户去获取Unity的编译命令文件，更好的做法应该是直接
        public static void Compile(string compileArgFile)
        {
#if UNITY_EDITOR_OSX
            var monoPath = Path.Combine(Path.GetDirectoryName(typeof(UnityEngine.Debug).Module.FullyQualifiedName),
                "../MonoBleedingEdge/bin/mono");
            var mcsPath = Path.Combine(Path.GetDirectoryName(typeof(UnityEngine.Debug).Module.FullyQualifiedName),
                "../MonoBleedingEdge/lib/mono/4.5/mcs.exe");
            if(!File.Exists(monoPath))
            {
                monoPath = Path.Combine(Path.GetDirectoryName(typeof(UnityEngine.Debug).Module.FullyQualifiedName),
                    "../../MonoBleedingEdge/bin/mono");
                mcsPath = Path.Combine(Path.GetDirectoryName(typeof(UnityEngine.Debug).Module.FullyQualifiedName),
                    "../../MonoBleedingEdge/lib/mono/4.5/mcs.exe");
            }
#elif UNITY_EDITOR_WIN
            var monoPath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName),
                "Data/MonoBleedingEdge/bin/mono.exe");
            var mcsPath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName),
                "Data/MonoBleedingEdge/lib/mono/4.5/mcs.exe");
#endif
            if (!File.Exists(monoPath))
            {
                UnityEngine.Debug.LogError("can not find mono!");
            }

            Process compileProcess = new Process();
            compileProcess.StartInfo.FileName = monoPath;
            compileProcess.StartInfo.Arguments = "\"" + mcsPath + "\" " + "@" + compileArgFile;
            compileProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            compileProcess.StartInfo.RedirectStandardOutput = true;
            compileProcess.StartInfo.RedirectStandardError = true;
            compileProcess.StartInfo.UseShellExecute = false;
            compileProcess.StartInfo.CreateNoWindow = true;
            compileProcess.Start();

            //UnityEngine.Debug.Log(hotfix_injection.StartInfo.FileName);
            //UnityEngine.Debug.Log(hotfix_injection.StartInfo.Arguments);

            while (!compileProcess.StandardError.EndOfStream)
            {
                var line = compileProcess.StandardError.ReadLine();
                if(!line.Contains("warning"))
                {
                    UnityEngine.Debug.LogError(line);
                }
                
            }

            while (!compileProcess.StandardOutput.EndOfStream)
            {
                UnityEngine.Debug.Log(compileProcess.StandardOutput.ReadLine());
            }
            
            compileProcess.WaitForExit();
        }

        //生成特定平台的patch
        public static void GenPlatformPatch(Platform platform, string patchOutputDir,
            string corePath = "./Assets/Plugins/IFix.Core.dll")
        {
            var outputDir = "Temp/ifix";
            Directory.CreateDirectory("Temp");
            Directory.CreateDirectory(outputDir);
#if UNITY_2018_3_OR_NEWER
            ScriptCompilationSettings scriptCompilationSettings = new ScriptCompilationSettings();
            if (platform == Platform.android)
            {
                scriptCompilationSettings.group = BuildTargetGroup.Android;
                scriptCompilationSettings.target = BuildTarget.Android;
            }
            else if(platform == Platform.ios)
            {
                scriptCompilationSettings.group = BuildTargetGroup.iOS;
                scriptCompilationSettings.target = BuildTarget.iOS;
            }
            else
            {
                scriptCompilationSettings.group = BuildTargetGroup.Standalone;
                scriptCompilationSettings.target = BuildTarget.StandaloneWindows;
            }

            ScriptCompilationResult scriptCompilationResult = PlayerBuildInterface.CompilePlayerScripts(scriptCompilationSettings, outputDir);

            foreach (var assembly in injectAssemblys)
            {
                GenPatch(assembly, string.Format("{0}/{1}.dll", outputDir, assembly),
                    "./Assets/Plugins/IFix.Core.dll", string.Format("{0}{1}.patch.bytes", patchOutputDir, assembly));
            }
#else
            throw new NotImplementedException();
            //var compileArgFile = "Temp/ifix/unity_" + platform + "_compile_argument";
            //var tmpDllPath = "Temp/ifix/Assembly-CSharp.dll";
            //File.WriteAllText(compileArgFile, getCompileArguments(platform, tmpDllPath));
            //先编译dll到Temp目录下
            //Compile(compileArgFile);
            //对编译后的dll生成补丁
            //GenPatch("Assembly-CSharp", tmpDllPath, corePath, patchPath);

            //File.Delete(compileArgFile);
            //File.Delete(tmpDllPath);
            //File.Delete(tmpDllPath + ".mdb");
#endif
        }

        //把方法签名写入文件
        //由于目前不支持泛型函数的patch，所以函数签名为方法名+参数类型
        static void writeMethods(BinaryWriter writer, List<MethodInfo> methods)
        {
            var methodGroups = methods.GroupBy(m => m.DeclaringType).ToList();
            writer.Write(methodGroups.Count);
            foreach (var methodGroup in methodGroups)
            {
                writer.Write(GetCecilTypeName(methodGroup.Key));
                writer.Write(methodGroup.Count());
                foreach (var method in methodGroup)
                {
                    writer.Write(method.Name);
                    writer.Write(GetCecilTypeName(method.ReturnType));
                    writer.Write(method.GetParameters().Length);
                    foreach (var parameter in method.GetParameters())
                    {
                        writer.Write(parameter.IsOut);
                        writer.Write(GetCecilTypeName(parameter.ParameterType));
                    }
                }
            }
        }

        /// <summary>
        /// 生成patch
        /// </summary>
        /// <param name="assembly">程序集名，用来过滤配置</param>
        /// <param name="assemblyCSharpPath">程序集路径</param>
        /// <param name="corePath">IFix.Core.dll所在路径</param>
        /// <param name="patchPath">生成的patch的保存路径</param>
        public static void GenPatch(string assembly, string assemblyCSharpPath
            = "./Library/ScriptAssemblies/Assembly-CSharp.dll", 
            string corePath = "./Assets/Plugins/IFix.Core.dll", string patchPath = "Assembly-CSharp.patch.bytes")
        {
            var patchMethods = Configure.GetTagMethods(typeof(PatchAttribute), assembly).ToList();
            var genericMethod = patchMethods.FirstOrDefault(m => m.IsGenericMethodDefinition || m.IsGenericMethod);
            if (genericMethod != null)
            {
                throw new InvalidDataException("not support generic method: " + genericMethod);
            }

            if (patchMethods.Count == 0)
            {
                return;
            }

            var newMethods = Configure.GetTagMethods(typeof(InterpretAttribute), assembly).ToList();
            genericMethod = newMethods.FirstOrDefault(m => m.IsGenericMethodDefinition || m.IsGenericMethod);
            if (genericMethod != null)
            {
                throw new InvalidDataException("not support generic method: " + genericMethod);
            }

            var processCfgPath = "./process_cfg";

            using (BinaryWriter writer = new BinaryWriter(new FileStream(processCfgPath, FileMode.Create,
                FileAccess.Write)))
            {
                writeMethods(writer, patchMethods);
                writeMethods(writer, newMethods);
            }

            List<string> args = new List<string>() { "-patch", corePath, assemblyCSharpPath, "null",
                processCfgPath, patchPath };

            foreach (var path in
                (from asm in AppDomain.CurrentDomain.GetAssemblies()
                    select Path.GetDirectoryName(asm.ManifestModule.FullyQualifiedName)).Distinct())
            {
                try
                {
                    //UnityEngine.Debug.Log("searchPath:" + path);
                    args.Add(path);
                }
                catch { }
            }

            GetAssetsDllPaths(args, "Assets");

            CallIFix(args);

            File.Delete(processCfgPath);

            AssetDatabase.Refresh();
        }

        [MenuItem("InjectFix/Fix", false, 2)]
        public static void Patch()
        {
            foreach (var assembly in injectAssemblys)
            {
                GenPatch(Path.GetFileNameWithoutExtension(assembly), assembly, 
                    "./Assets/Plugins/IFix.Core.dll", string.Format("{0}.patch.bytes", assembly));
            }
        }

#if UNITY_2018_3_OR_NEWER
        [MenuItem("InjectFix/Fix(Android)", false, 3)]
        public static void CompileToAndroid()
        {
            EditorUtility.DisplayProgressBar("Generate Patch for Android", "patching...", 0);
            GenPlatformPatch(Platform.android, "");
            EditorUtility.ClearProgressBar();
        }

        [MenuItem("InjectFix/Fix(IOS)", false, 4)]
        public static void CompileToIOS()
        {
            EditorUtility.DisplayProgressBar("Generate Patch for IOS", "patching...", 0);
            GenPlatformPatch(Platform.ios, "");
            EditorUtility.ClearProgressBar();
        }
#endif


        public static void GenPatchs(BuildTarget target,string outputdir)
        {
            Platform platform;
            if (target == BuildTarget.Android)
            {
                platform = Platform.android;
            }
            else if (target == BuildTarget.iOS)
            {
                platform = Platform.ios;
            }
            else
            {
                throw new ArgumentOutOfRangeException(target.ToString(), "only suport android and ios platform!");
            }
            string f_dll;
            string s_dll;
            if (BuildAssembly(platform, "ifix_patch", out f_dll, out s_dll))
            {
                
                if (Directory.Exists(outputdir))
                {
                    FileUtil.DeleteFileOrDirectory(outputdir);
                }
                Directory.CreateDirectory(outputdir);

                GenPatch("Assembly-CSharp-firstpass",
                    f_dll,
                    "./Assets/Plugins/IFix.Core.dll",
                    outputdir + "Assembly-CSharp-firstpass.patch.bytes");

                GenPatch("Assembly-CSharp",
                    s_dll,
                    "./Assets/Plugins/IFix.Core.dll",
                    outputdir + "Assembly-CSharp.patch.bytes");

            }
            for (int i = 2; i < injectAssemblys.Length; i++)
            {
                var assembly = injectAssemblys[i];
                GenPatch(Path.GetFileNameWithoutExtension(assembly),
                    assembly,
                    "./Assets/Plugins/IFix.Core.dll",
                    outputdir + string.Format("{0}.patch.bytes", Path.GetFileNameWithoutExtension(assembly)) );
            }
        }
        public static bool BuildAssembly(Platform platform, string tempdir, out string firstDllPath, out string secendDllPath)
        {
            string f_tmp_tpl;
            string s_tmp_tpl;
            string f_dll;
            string s_dll;
            if (CreateBuildAssemblyArgsFile(platform, tempdir, out f_tmp_tpl, out s_tmp_tpl, out f_dll, out s_dll))
            {
                Compile(f_tmp_tpl);
                if (!File.Exists(f_dll)) throw new FileNotFoundException(f_dll);

                Compile(s_tmp_tpl);
                if (!File.Exists(f_dll)) throw new FileNotFoundException(s_dll);

                File.Delete(f_tmp_tpl);
                File.Delete(s_tmp_tpl);
            }
            firstDllPath = f_dll;
            secendDllPath = s_dll;
            return true;
        }
        static bool CreateBuildAssemblyArgsFile(Platform platform, string tempdir, out string f_tmp_tpl, out string s_tmp_tpl, out string f_dll, out string s_dll)
        {
            Directory.CreateDirectory("Temp");
            Directory.CreateDirectory("Temp/" + tempdir);


            //First args file
            f_tmp_tpl = "Temp/" + tempdir + "/tmp." + platform + ".Assembly-CSharp-firstpass.tpl";
            var f_src_tpl = "IFixToolKit/" + platform + ".Assembly-CSharp-firstpass.tpl";
            f_dll = "./Temp/" + tempdir + "/Assembly-CSharp-firstpass.dll";
            var f_args = GetCompileArguments(f_tmp_tpl, f_src_tpl, f_dll, true);
            File.WriteAllText(f_tmp_tpl, f_args);

            //Secend args file
            s_tmp_tpl = "Temp/" + tempdir + "/tmp." + platform + ".Assembly-CSharp.tpl";
            var s_src_tpl = "IFixToolKit/" + platform + ".Assembly-CSharp.tpl";
            s_dll = "./Temp/" + tempdir + "/Assembly-CSharp.dll";
            var s_args = GetCompileArguments(s_tmp_tpl, s_src_tpl, s_dll, false);
            s_args += "\n-r:'" + f_dll + "'";
            File.WriteAllText(s_tmp_tpl, s_args);

            return true;
        }


    }
}
