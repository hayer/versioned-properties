using Castle.DynamicProxy;
using System;
using System.Collections.Generic;

namespace PlanConsole
{
    public interface IId
    {
        Guid Id { get; }
    }

    public class DataObject : IId
    {
        public DataObject()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; }
        public virtual string Name { get; set; }
        public virtual int Counter { get; set; }
    }

    class VersionedPropertyInterceptor : IInterceptor
    {
        public class InternalVersionContext : IDisposable
        {
            private readonly VersionedPropertyInterceptor versionedPropertyInterceptor;

            internal InternalVersionContext(int version, VersionedPropertyInterceptor versionedPropertyInterceptor)
            {
                Version = version;
                this.versionedPropertyInterceptor = versionedPropertyInterceptor;
            }

            public int Version { get; }

            public void Dispose()
            {
                versionedPropertyInterceptor.VersionContext = null;
            }
        }

        public VersionedPropertyInterceptor()
        {
            VersionContext = null;
        }

        public const int BaseVersion = 0;
        public InternalVersionContext VersionContext { get; private set; }
        public int Version => VersionContext == null ? BaseVersion : VersionContext.Version;
        private int _nextPropertyId = 0;
        private Dictionary<(Guid, int, int), object> ValueMap { get; } = new Dictionary<(Guid, int, int), object>();
        private Dictionary<(Guid, string), int> PropertyMap { get; } = new Dictionary<(Guid, string), int>();

        public InternalVersionContext OpenVersion(int version)
        {
            VersionContext = new InternalVersionContext(version, this);
            return VersionContext;
        }

        private void InterceptSetter(IInvocation invocation)
        {
            var id = (invocation.InvocationTarget as IId).Id;
            var propertyId = GetPropertyId(invocation);
            var version = VersionContext?.Version ?? BaseVersion;

            var value = invocation.Arguments[0];

            var key = (id, version, propertyId);
            ValueMap[key] = value;
        }

        private void InterceptGetter(IInvocation invocation)
        {
            // HACK: ugly assumtion that the metadata token of the setter is always +1 
            //       over the getter.
            var id = (invocation.InvocationTarget as IId).Id;
            var propertyId = GetPropertyId(invocation);
            var version = VersionContext?.Version ?? BaseVersion;

            var key = (id, version, propertyId);
            var baseKey = (id, BaseVersion, propertyId); ;

            if (ValueMap.ContainsKey(key))
            {
                invocation.ReturnValue = ValueMap[key];
            }
            else if(ValueMap.ContainsKey(baseKey))
            {
                invocation.ReturnValue = ValueMap[baseKey];
            }
            else
            {
                invocation.Proceed();
            }
        }

        private int GetPropertyId(IInvocation invocation)
        {
            var propName = invocation.Method.Name.Substring(4);
            var key = (invocation.TargetType.GUID, propName);

            if(!PropertyMap.ContainsKey(key))
            {
                PropertyMap[key] = _nextPropertyId++;
            }

            return PropertyMap[key];
        }

        public void Intercept(IInvocation invocation)
        {
            // is it trying to set a property?
            if (invocation.Method.Name.StartsWith("set_"))
            {
                InterceptSetter(invocation);
            }
            else if(invocation.Method.Name.StartsWith("get_"))
            {
                InterceptGetter(invocation);
            }
        }
    }


    class Program
    {
        static void Main(string[] args)
        {
            var pg = new ProxyGenerator();
            var interceptor = new VersionedPropertyInterceptor();
            var d1 = pg.CreateClassProxy<DataObject>(interceptor);

            d1.Name = "Hei";

            using (interceptor.OpenVersion(1))
            {
                d1.Name = "Verden";
                PrintData(d1, interceptor);
            }

            PrintData(d1, interceptor);
            d1.Counter--;
            PrintData(d1, interceptor);

            using (interceptor.OpenVersion(1))
            {
                d1.Name = "Verden";
                PrintData(d1, interceptor);
            }
        }

        static void PrintData(DataObject dataObject, VersionedPropertyInterceptor interceptor)
        {
            Console.WriteLine($"DataObject v{interceptor.Version}");
            Console.WriteLine($"\tName: {dataObject.Name}");
            Console.WriteLine($"\tCounter: {dataObject.Counter}");
        }
    }
}
