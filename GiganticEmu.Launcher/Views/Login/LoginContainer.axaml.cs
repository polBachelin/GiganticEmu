﻿using ReactiveUI;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using Avalonia.Controls;

namespace GiganticEmu.Launcher;

public partial class LoginContainer : ReactiveUserControl<LoginContainerViewModel>
{
    public LoginContainer()
    {
        InitializeComponent();
        ViewModel = new LoginContainerViewModel();

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel,
                viewModel => viewModel.User,
                view => view.PageLogin.ViewModel!.User
            );

            this.Bind(ViewModel,
                viewModel => viewModel.User,
                view => view.PageRegister.ViewModel!.User
            );

            this.OneWayBind(ViewModel,
                viewModel => viewModel.CurrentPage,
                view => view.PageRegister.IsVisible,
                value => value == LoginContainerViewModel.Page.Register
            );

            this.OneWayBind(ViewModel,
                viewModel => viewModel.CurrentPage,
                view => view.PageLogin.IsVisible,
                value => value == LoginContainerViewModel.Page.Login
            );

            Observable.FromEventPattern(PageLogin, nameof(PageLogin.RegisterClicked))
                .Do(_ => ViewModel.CurrentPage = LoginContainerViewModel.Page.Register)
                .Subscribe()
                .DisposeWith(disposables);

            Observable.FromEventPattern(PageRegister, nameof(PageRegister.LoginClicked))
                .Do(_ => ViewModel.CurrentPage = LoginContainerViewModel.Page.Login)
                .Subscribe()
                .DisposeWith(disposables);
        });
    }
}