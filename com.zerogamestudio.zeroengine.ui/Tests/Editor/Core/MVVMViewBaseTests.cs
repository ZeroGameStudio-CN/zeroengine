using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.UI.MVVM;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    [Category("Unit")]
    public sealed class MVVMViewBaseTests
    {
        private GameObject _gameObject;
        private ProbeView _view;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("MVVMViewBase.Tests");
            _view = _gameObject.AddComponent<ProbeView>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }
        }

        [Test]
        public void ManagedLifecycle_InitializesBeforeBindingAndRefreshesSameModelOnEachOpen()
        {
            _view.CreateForTest();
            var model = _view.Model;
            _view.OpenForTest();
            _view.OpenForTest();

            Assert.That(_view.Model, Is.SameAs(model));
            Assert.That(model.Calls, Is.EqualTo(new[] { "initialize", "bind", "refresh", "refresh" }));
            Assert.That(_view.DisplayedValue, Is.EqualTo(2));
        }

        [Test]
        public void ViewDestroy_UnbindsBeforeModelDisposalAndReleasesStandaloneModel()
        {
            var model = new ProbeViewModel();
            _view.SetStandaloneForTest(model);
            Assert.That(model.Calls, Is.EqualTo(new[] { "bind" }));
            model.Value.Value = 7;
            Assert.That(_view.DisplayedValue, Is.EqualTo(7));

            _view.DestroyForTest();

            Assert.That(model.Calls, Is.EqualTo(new[] { "bind", "dispose" }));
            Assert.That(_view.DisplayedValue, Is.EqualTo(7), "Disposal must no longer update a destroyed view.");
            model.Value.Value = 9;
            Assert.That(_view.DisplayedValue, Is.EqualTo(7));
        }

        [Test]
        public void ViewDestroy_BeforeInitialization_IsSafe()
        {
            Assert.DoesNotThrow(_view.DestroyForTest);
        }

        public sealed class ProbeView : MVVMViewBase<ProbeViewModel>
        {
            public ProbeViewModel Model => ViewModel;
            public int DisplayedValue { get; private set; }
            public void CreateForTest() => OnCreate();
            public void OpenForTest() => OnOpen();
            // EditMode does not provide a runtime MonoBehaviour destruction callback;
            // drive the same hook as the managed UI lifetime, as with Create/Open above.
            public void DestroyForTest() => OnViewDestroy();

            public void SetStandaloneForTest(ProbeViewModel model)
            {
                SetViewModel(model);
                SetupBindings();
            }

            protected override void SetupBindings()
            {
                ViewModel.Calls.Add("bind");
                BindingContext.Bind(ViewModel.Value, value => DisplayedValue = value);
            }
        }

        public sealed class ProbeViewModel : ViewModelBase
        {
            public List<string> Calls { get; } = new();
            public BindableProperty<int> Value { get; } = new();
            public override void Initialize() => Calls.Add("initialize");

            public override void Refresh()
            {
                Calls.Add("refresh");
                Value.Value++;
            }

            protected override void OnDispose()
            {
                Calls.Add("dispose");
                Value.Value = -1;
            }
        }
    }
}
