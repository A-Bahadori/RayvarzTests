using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace TestNet472
{
    public class ExceptionDetail
    {
        public string Message { get; set; }
        public string ExceptionType { get; set; }
        public string Source { get; set; }
        public DateTime Timestamp { get; set; }
        public string StackTrace { get; set; }
        public List<StackFrameDetail> Frames { get; set; }
        public ExceptionDetail InnerException { get; set; }
        public StackFrameDetail RootCause { get; set; } // علت اصلی خطا
        public string ErrorCode { get; set; } // کد خطا
        public Dictionary<string, string> AdditionalData { get; set; } // اطلاعات اضافی
    }

    public class StackFrameDetail
    {
        public string FileName { get; set; }
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
        public string MethodName { get; set; }
        public string ClassName { get; set; }
        public string Parameters { get; set; }
        public bool IsUserCode { get; set; } // آیا کد کاربر است یا سیستمی
        public string Namespace { get; set; }
    }

    public static class ExceptionHandler
    {
        private static readonly HashSet<string> SystemNamespaces = new HashSet<string>
        {
            "System",
            "Microsoft",
            "mscorlib"
        };

        public static ExceptionDetail CaptureException(Exception exception)
        {
            if (exception == null) return null;

            var exceptionDetail = new ExceptionDetail
            {
                Message = exception.Message,
                ExceptionType = exception.GetType().FullName,
                Source = exception.Source,
                Timestamp = DateTime.UtcNow,
                StackTrace = exception.StackTrace,
                Frames = new List<StackFrameDetail>(),
                AdditionalData = new Dictionary<string, string>(),
                ErrorCode = GenerateErrorCode(exception),
            };

            // استخراج جزئیات StackFrame
            var stackTrace = new StackTrace(exception, true);
            var frames = stackTrace.GetFrames() ?? Array.Empty<StackFrame>();

            // StackTrace st = new StackTrace(exception, true);
            // StackFrame[] frames2 = st.GetFrames();
            //
            // if (frames2 != null)
            // {
            //     var frame = frames2[0];
            // }

            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                var frameDetail = CreateStackFrameDetail(frame);
                exceptionDetail.Frames.Add(frameDetail);
            }

            // یافتن علت اصلی خطا
            exceptionDetail.RootCause = FindRootCause(exceptionDetail.Frames);

            // بررسی InnerException
            if (exception.InnerException != null)
            {
                exceptionDetail.InnerException = CaptureException(exception.InnerException);
            }

            // اضافه کردن اطلاعات مفید اضافی
            AddAdditionalInformation(exceptionDetail, exception);

            return exceptionDetail;
        }

        private static StackFrameDetail CreateStackFrameDetail(StackFrame frame)
        {
            var method = frame.GetMethod();
            var parameters = new StringBuilder();

            if (method?.GetParameters() != null)
            {
                parameters.Append(string.Join(", ",
                    method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")));
            }

            var className = method?.DeclaringType?.FullName ?? "";
            var isUserCode = !SystemNamespaces.Any(ns => className.StartsWith(ns));
            var nameSpace = method?.DeclaringType?.Namespace ?? "";

            return new StackFrameDetail
            {
                FileName = frame.GetFileName(),
                LineNumber = frame.GetFileLineNumber(),
                ColumnNumber = frame.GetFileColumnNumber(),
                MethodName = method?.Name,
                ClassName = className,
                Parameters = parameters.ToString(),
                IsUserCode = isUserCode,
                Namespace = nameSpace
            };
        }

        private static StackFrameDetail FindRootCause(List<StackFrameDetail> frames)
        {
            // یافتن اولین فریم کد کاربر (به عنوان علت اصلی)
            return frames.FirstOrDefault(f => f.IsUserCode && !string.IsNullOrEmpty(f.FileName))
                   ?? frames.FirstOrDefault();
        }

        private static string GenerateErrorCode(Exception ex)
        {
            // ایجاد کد خطای منحصر به فرد
            return $"E{ex.GetHashCode():X8}";
        }

        private static void AddAdditionalInformation(ExceptionDetail detail, Exception ex)
        {
            detail.AdditionalData["MachineName"] = Environment.MachineName;
            detail.AdditionalData["OSVersion"] = Environment.OSVersion.ToString();
            detail.AdditionalData["ProcessId"] = Process.GetCurrentProcess().Id.ToString();

            // اضافه کردن اطلاعات خاص نوع خطا
            if (ex is System.IO.IOException)
            {
                detail.AdditionalData["ErrorType"] = "IO Error";
            }
            else if (ex is SqlException sqlEx)
            {
                detail.AdditionalData["ErrorType"] = "Database Error";
                detail.AdditionalData["SqlErrorNumber"] = sqlEx.Number.ToString();
            }
        }

        public static string FormatExceptionDetail(ExceptionDetail detail, bool includeFrames = true)
        {
            if (detail == null) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("=== Exception Details ===");
            sb.AppendLine($"Error Code: {detail.ErrorCode}");
            sb.AppendLine($"Type: {detail.ExceptionType}");
            sb.AppendLine($"Message: {detail.Message}");
            sb.AppendLine($"Timestamp: {detail.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");

            if (detail.RootCause != null)
            {
                sb.AppendLine("\n=== Root Cause ===");
                sb.AppendLine($"File: {detail.RootCause.FileName}");
                sb.AppendLine($"Line: {detail.RootCause.LineNumber}");
                sb.AppendLine($"Method: {detail.RootCause.ClassName}.{detail.RootCause.MethodName}");
            }

            if (includeFrames && detail.Frames?.Count > 0)
            {
                sb.AppendLine("\n=== Stack Trace ===");
                foreach (var frame in detail.Frames)
                {
                    if (frame.IsUserCode)
                    {
                        sb.AppendLine($"[User Code] {frame.ClassName}.{frame.MethodName}");
                    }
                    else
                    {
                        sb.AppendLine($"[System] {frame.ClassName}.{frame.MethodName}");
                    }

                    if (!string.IsNullOrEmpty(frame.FileName))
                    {
                        sb.AppendLine($"  at {frame.FileName}:line {frame.LineNumber}");
                    }
                }
            }

            if (detail.AdditionalData.Count > 0)
            {
                sb.AppendLine("\n=== Additional Information ===");
                foreach (var kvp in detail.AdditionalData)
                {
                    sb.AppendLine($"{kvp.Key}: {kvp.Value}");
                }
            }

            if (detail.InnerException != null)
            {
                sb.AppendLine("\n=== Inner Exception ===");
                sb.AppendLine(FormatExceptionDetail(detail.InnerException, includeFrames));
            }

            return sb.ToString();
        }
    }


    public class ExceptionLevel
    {
        public int Level { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
    }

    public class CustomException : Exception
    {
        public List<ExceptionLevel> ExceptionLevels { get; private set; }

        public CustomException()
        {
            ExceptionLevels = new List<ExceptionLevel>();

            // Level 2
            ExceptionLevels.Add(new ExceptionLevel
            {
                Level = 2,
                Message = "Input string was not in a correct format.",
                StackTrace =
                    @"at System.Number.StringToNumber(String str, NumberStyles options, NumberBuffer& number, NumberFormatInfo info, Boolean parseDecimal)
at System.Number.ParseInt32(String s, NumberStyles style, NumberFormatInfo info)
at BPMS.ControlBehaviour.mstrTestUserControl354fa1559f3c4becb086b29e00b42a2a.Methode1(IUnitOfWork unitOfWork, Object rayUserControl, MstrTest formObject, String validationGroup, PostbackControl postbackControl, Boolean isPostBack, List1 externalParams)"
            });

            // Level 1
            ExceptionLevels.Add(new ExceptionLevel
            {
                Level = 1,
                Message = "Exception has been thrown by the target of an invocation.",
                StackTrace =
                    @"at System.RuntimeMethodHandle.InvokeMethod(Object target, Object[] arguments, Signature sig, Boolean constructor)
at System.Reflection.RuntimeMethodInfo.UnsafeInvokeInternal(Object obj, Object[] parameters, Object[] arguments)
at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
at System.Reflection.MethodBase.Invoke(Object obj, Object[] parameters)
at Ray.BPMS.Infrastructure.RayCompiler.Run(Assembly assembly, String fullclassName, String methodeName, Object[] parameters) in F:\src\newBPMS\source\Rayvarz.BPMS.Infrastructure\RayCompiler.cs:line 160"
            });

            // Level 0
            ExceptionLevels.Add(new ExceptionLevel
            {
                Level = 0,
                Message =
                    "Error Invoke methode: Methode1 className:BPMS.ControlBehaviour.mstrTestUserControl354fa1559f3c4becb086b29e00b42a2a assembly:BPMS.ControlBehaviour.mstrTestUserControl354fa1559f3c4becb086b29e00b42a2a_93ca5c6e-5223-4e9f-82a1-49f6f00f0fe2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null\nException has been thrown by the target of an invocation.Input string was not in a correct format.",
                StackTrace =
                    @"at Ray.BPMS.Infrastructure.RayCompiler.Run(Assembly assembly, String fullclassName, String methodeName, Object[] parameters) in F:\src\newBPMS\source\Rayvarz.BPMS.Infrastructure\RayCompiler.cs:line 169
at Ray.BPMS.Infrastructure.RayCompiler.RunInMemory(String[] referencedAssemblies, String[] sources, String fullclassName, String methodeName, Object[] parameters) in F:\src\newBPMS\source\Rayvarz.BPMS.Infrastructure\RayCompiler.cs:line 107
at Ray.BPMS.Services.RuleService.RunRre(Object[] args, Rule rag, String fullClassName, String assemblyPath, String[] references, String formControlCast) in F:\src\newBPMS\source\Rayvarz.BPMS.RuleService\RuleService.cs:line 635
at Ray.BPMS.Services.RuleService.RunRre(Object[] args, Rule rag, String applicationName, String uri) in F:\src\newBPMS\source\Rayvarz.BPMS.RuleService\RuleService.cs:line 617
at Ray.BPMS.Services.RuleService.InVokeControlBehaviour(Rule rag, AjaxFormState formState, IFormObject formObject, PostbackControl postbackControl, Boolean isPostback, String applicationName, String uri, List1 externalParams) in F:\src\newBPMS\source\Rayvarz.BPMS.RuleService\RuleService.cs:line 594
at Ray.BPMS.Services.RuleService.RunControlBehaviour(Rule codeItem, AjaxFormState formState, IFormObject formObject, PostbackControl postbackControl, Boolean isPostback, String applicationName, String uri, List1 externalParams) in F:\src\newBPMS\source\Rayvarz.BPMS.RuleService\RuleService.cs:line 215
at Ray.BPMS.Framework.Service.RuleFrameworkService.RunCodeItem(Rule codeItem, AjaxFormState formState, IFormObject formObject, PostbackControl postbackControl, Boolean isPostback, String applicationName, String uri, List1 externalParams) in F:\src\newBPMS\source\Rayvarz.BPMS.Framework.Service\RuleFrameworkService.cs:line 154
at Ray.BPMS.Web.Framework.FormManager.RunCodeItem(PhysicalFormBinding physicalFormBinding, Rule codeItem, IFormObject formObject, AjaxFormState formState, PostbackControl postbackControl, Boolean isPostback, IUnitOfWork unitOfWork) in F:\src\newBPMS\Source\Rayvarz.BPMS.Web.Framework\FormManager.cs:line 2044"
            });
        }

        public override string Message => string.Join("\n\n",
            ExceptionLevels.OrderByDescending(x => x.Level).Select(x => $"Level ({x.Level}) Message: {x.Message}"));

        public override string StackTrace => string.Join("\n\n",
            ExceptionLevels.OrderByDescending(x => x.Level)
                .Select(x => $"Level ({x.Level}) StackTrace:\n{x.StackTrace}"));
    }

    
    /// <summary>
    /// کلاس Exception شخصی برای مدیریت خطاهای مربوط به فرمت ورودی در BPMS
    /// </summary>
    [Serializable]
    public class BPMSInputFormatException : Exception
    {
        public int ErrorLevel { get; private set; }
        public string MethodName { get; private set; }
        public string ClassName { get; private set; }
        public string AssemblyName { get; private set; }
        
        /// <summary>
        /// سازنده پیش‌فرض
        /// </summary>
        public BPMSInputFormatException() : base("BPMS Input Format Exception occurred")
        {
        }

        /// <summary>
        /// سازنده با پیام خطا
        /// </summary>
        public BPMSInputFormatException(string message) : base(message)
        {
        }

        /// <summary>
        /// سازنده با پیام خطا و خطای داخلی
        /// </summary>
        public BPMSInputFormatException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// سازنده کامل با تمام جزئیات خطا
        /// </summary>
        public BPMSInputFormatException(string message, Exception innerException, 
            string methodName, string className, string assemblyName, int errorLevel = 0) 
            : base(message, innerException)
        {
            MethodName = methodName;
            ClassName = className;
            AssemblyName = assemblyName;
            ErrorLevel = errorLevel;
        }

        /// <summary>
        /// سازنده برای Serialization
        /// </summary>
        protected BPMSInputFormatException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
            ErrorLevel = info.GetInt32(nameof(ErrorLevel));
            MethodName = info.GetString(nameof(MethodName));
            ClassName = info.GetString(nameof(ClassName));
            AssemblyName = info.GetString(nameof(AssemblyName));
        }

        /// <summary>
        /// متد برای Serialization
        /// </summary>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(ErrorLevel), ErrorLevel);
            info.AddValue(nameof(MethodName), MethodName);
            info.AddValue(nameof(ClassName), ClassName);
            info.AddValue(nameof(AssemblyName), AssemblyName);
        }

        /// <summary>
        /// نمایش کامل اطلاعات خطا در تمام سطوح
        /// </summary>
        public string GetDetailedErrorMessage()
        {
            StringBuilder sb = new StringBuilder();

            // اطلاعات خود خطا
            sb.AppendLine($"Level ({ErrorLevel}) Message: {Message}");
            sb.AppendLine($"Level ({ErrorLevel}) StackTrace: {StackTrace}");
            sb.AppendLine($"Method: {MethodName}");
            sb.AppendLine($"Class: {ClassName}");
            sb.AppendLine($"Assembly: {AssemblyName}");
            sb.AppendLine();

            // بررسی خطاهای داخلی
            int level = ErrorLevel + 1;
            Exception innerEx = InnerException;
            
            while (innerEx != null)
            {
                sb.AppendLine($"Level ({level}) Message: {innerEx.Message}");
                sb.AppendLine($"Level ({level}) StackTrace: {innerEx.StackTrace}");
                sb.AppendLine();
                
                innerEx = innerEx.InnerException;
                level++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// سازنده کمکی برای ایجاد خطا از خطای فرمت ورودی
        /// </summary>
        public static BPMSInputFormatException FromFormatException(
            FormatException formatException, 
            string methodName, 
            string className, 
            string assemblyName)
        {
            // ایجاد خطای لایه 2 (خطای اصلی)
            var level2Exception = formatException;
            
            // ایجاد خطای لایه 1 (خطای Reflection)
            var level1Exception = new TargetInvocationException(
                "Exception has been thrown by the target of an invocation.", 
                level2Exception);
            
            // ایجاد خطای لایه 0 (خطای BPMS)
            string message = $"Error Invoke methode: {methodName} className:{className} assembly:{assemblyName}, " +
                             $"Version=0.0.0.0, Culture=neutral, PublicKeyToken=null " +
                             $"Exception has been thrown by the target of an invocation.{formatException.Message}";
            
            return new BPMSInputFormatException(message, level1Exception, methodName, className, assemblyName, 0);
        }
    }
    
    
    class Program
    {
        public static void TestMethod()
        {
            try
            {
                // عملیاتی که ممکن است خطای فرمت ایجاد کند
                string invalidInput = "این یک عدد نیست";
                int number = int.Parse(invalidInput);
            }
            catch (FormatException ex)
            {
                // تبدیل به خطای شخصی با تمام اطلاعات مورد نیاز
                throw BPMSInputFormatException.FromFormatException(
                    ex,
                    "Methode1",
                    "BPMS.ControlBehaviour.mstrTestUserControl354fa1559f3c4becb086b29e00b42a2a",
                    "BPMS.ControlBehaviour.mstrTestUserControl354fa1559f3c4becb086b29e00b42a2a_93ca5c6e-5223-4e9f-82a1-49f6f00f0fe2"
                );
            }
        }
        
        static void Main()
        {
            try
            {
                // کد شما که ممکن است خطا تولید کند
                //throw new InvalidOperationException("Test Exception");
                
                TestMethod();
            }
            catch (BPMSInputFormatException ex)
            {
                var exceptionDetail = ExceptionHandler.CaptureException(ex);
                
                // نمایش علت اصلی خطا
                var rootCause = exceptionDetail.RootCause;
                Console.WriteLine($"Main Caused Error:");
                Console.WriteLine($"File: {rootCause.FileName}");
                Console.WriteLine($"Line: {rootCause.LineNumber}");
                Console.WriteLine($"Method: {rootCause.ClassName}.{rootCause.MethodName}");

                // نمایش کامل جزئیات
                Console.WriteLine("\nTotal Error Detail:");
                Console.WriteLine(ExceptionHandler.FormatExceptionDetail(exceptionDetail));
            }
        }
    }
}

//----------------------------------------------------------------------------------------