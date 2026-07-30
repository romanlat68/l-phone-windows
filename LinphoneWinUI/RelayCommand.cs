using System;
using System.Windows.Input;

namespace LinphoneWinUI;

/// <summary>Простейшая ICommand-обёртка для привязки действий (напр. клик по значку в трее).</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
