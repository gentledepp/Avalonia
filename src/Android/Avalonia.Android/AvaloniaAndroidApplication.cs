using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.Runtime;
using Java.Nio.FileNio.Attributes;

namespace Avalonia.Android
{
    internal interface IAndroidApplication
    {
        ApplicationLifetime? Lifetime { get; set; }
        void SetupWithLifetime(Func<AppBuilder, AppBuilder> customizeAppBuilder);
    }

    public class AvaloniaAndroidApplication<TApp> : global::Android.App.Application, IAndroidApplication
        where TApp : Application, new()
    {
        private AppBuilder? _builder;

        ApplicationLifetime? IAndroidApplication.Lifetime { get; set; }

        protected AvaloniaAndroidApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();
            InitializeAppLifetime();
        }

        private void InitializeAppLifetime()
        {
            _builder = CreateAppBuilder();
            _builder = CustomizeAppBuilder(_builder);
        }

        protected virtual AppBuilder CreateAppBuilder() => AppBuilder.Configure<TApp>().UseAndroid();
        protected virtual AppBuilder CustomizeAppBuilder(AppBuilder builder) => builder;

        void IAndroidApplication.SetupWithLifetime(Func<AppBuilder, AppBuilder> customizeAppBuilder)
        {
            if (_builder is null)
                return;

            var b = _builder;
            _builder = null;

            var lifetime = new ApplicationLifetime();

            ((IAndroidApplication)this).Lifetime = lifetime;
            b = customizeAppBuilder(b);
            b.SetupWithLifetime(lifetime);
        }
    }
}
