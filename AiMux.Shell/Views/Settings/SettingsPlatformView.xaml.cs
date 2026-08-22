using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AiMux.Models;
using AiMux.Shell.ViewModels.Settings;

namespace AiMux.Shell.Views.Settings;

/// <summary>平台管理面板：左侧列表支持拖拽排序，且与主界面侧边栏共享同一份平台配置，实时同步</summary>
public partial class SettingsPlatformView : UserControl
{
    // 拖拽状态
    private PlatformInfo? _dragItem;
    private ListBoxItem? _dragSourceContainer;
    private ListBoxItem? _dropTargetContainer;
    private bool _insertAfter;
    private Point _dragStartPoint;

    public SettingsPlatformView() => InitializeComponent();

    private void PlatformsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _dragItem = GetPlatformFromEvent(PlatformsListBox, e.OriginalSource as DependencyObject);
    }

    private void PlatformsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDragVisual();
            return;
        }
        var diff = e.GetPosition(null) - _dragStartPoint;
        if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
        {
            _dragSourceContainer = GetContainerFromPlatform(PlatformsListBox, _dragItem);
            if (_dragSourceContainer != null)
                _dragSourceContainer.Opacity = 0.4;
            DragDrop.DoDragDrop(PlatformsListBox, _dragItem, DragDropEffects.Move);
            ResetDragVisual();
        }
    }

    private void PlatformsList_DragOver(object sender, DragEventArgs e)
    {
        var target = GetPlatformFromEvent(PlatformsListBox, e.OriginalSource as DependencyObject);
        var container = target == null ? null : GetContainerFromPlatform(PlatformsListBox, target);
        if (container == null)
        {
            ClearDropHighlight();
            return;
        }
        var pos = e.GetPosition(container);
        _insertAfter = pos.Y > container.ActualHeight / 2;
        if (_dropTargetContainer != container)
        {
            ClearDropHighlight();
            _dropTargetContainer = container;
            container.Background = new SolidColorBrush(Color.FromArgb(40, 0, 120, 212));
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void PlatformsList_Drop(object sender, DragEventArgs e)
    {
        var dragged = _dragItem;
        var target = GetPlatformFromEvent(PlatformsListBox, e.OriginalSource as DependencyObject);
        ResetDragVisual();
        if (dragged != null && target != null && dragged != target && DataContext is SettingsPlatformViewModel vm)
        {
            var from = vm.Platforms.IndexOf(dragged);
            var to = vm.Platforms.IndexOf(target);
            if (from >= 0 && to >= 0)
            {
                vm.Platforms.RemoveAt(from);
                // 移除后索引前移，按源在目标前/后分别计算插入点
                int insertAt = from < to
                    ? (_insertAfter ? to : to - 1)
                    : (_insertAfter ? to + 1 : to);
                vm.Platforms.Insert(insertAt, dragged);
                // 持久化顺序：触发 PlatformsChanged，主界面侧边栏随之刷新
                vm.SavePlatformsOrder();
            }
        }
        e.Handled = true;
    }

    private void ResetDragVisual()
    {
        if (_dragSourceContainer != null)
            _dragSourceContainer.Opacity = 1;
        ClearDropHighlight();
        _dragItem = null;
        _dragSourceContainer = null;
    }

    private void ClearDropHighlight()
    {
        if (_dropTargetContainer != null)
            _dropTargetContainer.Background = null;
        _dropTargetContainer = null;
    }

    private PlatformInfo? GetPlatformFromEvent(ListBox? lb, DependencyObject? source)
    {
        if (lb == null || source == null)
            return null;
        var container = source;
        while (container != null && !(container is ListBoxItem))
            container = VisualTreeHelper.GetParent(container);
        if (container is ListBoxItem item && item.Content is PlatformInfo pi)
            return pi;
        return null;
    }

    private ListBoxItem? GetContainerFromPlatform(ListBox? lb, PlatformInfo pi)
    {
        if (lb == null)
            return null;
        foreach (var item in lb.Items)
        {
            if (item is PlatformInfo p && p.Id == pi.Id)
                return lb.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
        }
        return null;
    }
}
