using System;
using System.IO;
using System.Text;

namespace DataBaseAsync
{
    public class ConsoleRedirector
    {
        private StringWriter _stringWriter;
        private TextWriter _originalOutput;
        private bool _isRedirecting;
        
        public event EventHandler<string> ConsoleOutput;
        
        public void Start()
        {
            if (_isRedirecting)
                return;
                
            _originalOutput = Console.Out;
            _stringWriter = new StringWriter();
            
            // 创建一个自定义的TextWriter来捕获输出
            var customWriter = new CustomTextWriter(_stringWriter, OnConsoleWrite);
            Console.SetOut(customWriter);
            
            _isRedirecting = true;
        }
        
        public void Stop()
        {
            if (!_isRedirecting)
                return;
                
            Console.SetOut(_originalOutput);
            _stringWriter?.Dispose();
            _isRedirecting = false;
        }
        
        private void OnConsoleWrite(string output)
        {
            if (!string.IsNullOrWhiteSpace(output))
            {
                ConsoleOutput?.Invoke(this, output.Trim());
            }
        }
    }
    
    public class CustomTextWriter : TextWriter
    {
        private readonly TextWriter _originalWriter;
        private readonly Action<string> _onWrite;
        private readonly StringBuilder _lineBuffer;
        
        public CustomTextWriter(TextWriter originalWriter, Action<string> onWrite)
        {
            _originalWriter = originalWriter;
            _onWrite = onWrite;
            _lineBuffer = new StringBuilder();
        }
        
        public override Encoding Encoding => _originalWriter.Encoding;
        
        public override void Write(char value)
        {
            _originalWriter.Write(value);
            
            if (value == '\n')
            {
                var line = _lineBuffer.ToString();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _onWrite?.Invoke(line);
                }
                _lineBuffer.Clear();
            }
            else if (value != '\r')
            {
                _lineBuffer.Append(value);
            }
        }
        
        public override void Write(string value)
        {
            _originalWriter.Write(value);
            
            if (!string.IsNullOrEmpty(value))
            {
                foreach (char c in value)
                {
                    if (c == '\n')
                    {
                        var line = _lineBuffer.ToString();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            _onWrite?.Invoke(line);
                        }
                        _lineBuffer.Clear();
                    }
                    else if (c != '\r')
                    {
                        _lineBuffer.Append(c);
                    }
                }
            }
        }
        
        public override void WriteLine(string value)
        {
            _originalWriter.WriteLine(value);
            
            if (!string.IsNullOrWhiteSpace(value))
            {
                _lineBuffer.Append(value);
                var line = _lineBuffer.ToString();
                _onWrite?.Invoke(line);
                _lineBuffer.Clear();
            }
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 处理剩余的缓冲内容
                if (_lineBuffer.Length > 0)
                {
                    var line = _lineBuffer.ToString();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _onWrite?.Invoke(line);
                    }
                }
                _originalWriter?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}