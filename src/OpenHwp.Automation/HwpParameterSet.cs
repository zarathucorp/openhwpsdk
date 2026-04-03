using System;

namespace OpenHwp.Automation
{
    public sealed class HwpParameterSet : IDisposable
    {
        private object _comObject;
        private bool _disposed;

        internal HwpParameterSet(object comObject)
        {
            _comObject = comObject ?? throw new ArgumentNullException(nameof(comObject));
        }

        internal object RawComObject
        {
            get
            {
                EnsureNotDisposed();
                return _comObject;
            }
        }

        public string SetId => Convert.ToString(((dynamic)RawComObject).SetID);

        public int Count => Convert.ToInt32(((dynamic)RawComObject).Count);

        public bool IsSet => Convert.ToBoolean(((dynamic)RawComObject).IsSet);

        public void SetItem(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Parameter name is required.", nameof(name));
            }

            ((dynamic)RawComObject).SetItem(name, value);
        }

        public object GetItem(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Parameter name is required.", nameof(name));
            }

            return ((dynamic)RawComObject).Item(name);
        }

        public bool ItemExists(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Parameter name is required.", nameof(name));
            }

            return Convert.ToBoolean(((dynamic)RawComObject).ItemExist(name));
        }

        public HwpParameterSet CreateChildSet(string name, string setId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Parameter name is required.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(setId))
            {
                throw new ArgumentException("Child set id is required.", nameof(setId));
            }

            var child = ((dynamic)RawComObject).CreateItemSet(name, setId);
            return new HwpParameterSet(child);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ComHelpers.SafeRelease(_comObject);
            _comObject = null;
            _disposed = true;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HwpParameterSet));
            }
        }
    }
}
