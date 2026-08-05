using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LinphoneWinUI.Interop;

namespace LinphoneWinUI.ViewModels;

/// <summary>Слот парковки: номер и что в нём стоит.</summary>
public sealed partial class ParkingSlotRow : ObservableObject
{
    public string Slot { get; }

    /// <summary>Слот занят — в нём ждёт звонок. Из dialog-info: confirmed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial bool Occupied { get; set; }

    /// <summary>Кто там стоит, если АТС сообщила. Пусто — просто «занят»: врать
    /// именем звонящего хуже, чем не показать его вовсе.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial string Caller { get; set; } = "";

    public string StateText => !Occupied ? Loc.S("Park_Free")
                             : Caller.Length > 0 ? Caller
                             : Loc.S("Park_Busy");

    public ParkingSlotRow(string slot) => Slot = slot;
}

/// <summary>
/// Парковка звонков. Смысл — «положить трубку так, чтобы взял любой»: секретарь паркует
/// звонок и объявляет по громкой связи, кто нужен, а тот забирает со своего аппарата.
/// Перевод для этого не годится — он требует знать заранее, кому именно отдавать.
///
/// Состояние слотов приходит той же подпиской «Event: dialog», что и BLF коллег:
/// на АТС каждый слот — обычный hint (park:501@parkedcalls), отдельного протокола нет.
/// </summary>
public partial class ParkingViewModel : ObservableObject
{
    private readonly ISipService _sip;

    /// <summary>Все слоты — для подписки.</summary>
    public ObservableCollection<ParkingSlotRow> Slots { get; } = new();

    /// <summary>Только занятые — для показа. Отдельная коллекция, а не фильтр видимости
    /// в разметке: сетка резервирует ячейку под каждый элемент, включая скрытый, и под
    /// заголовком зияла пустота на все двадцать слотов.</summary>
    public ObservableCollection<ParkingSlotRow> Occupied { get; } = new();

    /// <summary>Куда переводить звонок, чтобы припарковать (на АТС — parkext).</summary>
    public string ParkExtension { get; private set; } = "500";

    /// <summary>Хоть один слот занят. Пустую стоянку показывать незачем — она заняла бы
    /// половину окна ради двадцати одинаковых пустых плиток.</summary>
    [ObservableProperty] public partial bool AnyOccupied { get; set; }

    /// <summary>Сколько звонков стоит — для значка на вкладке.</summary>
    [ObservableProperty] public partial int OccupiedCount { get; set; }

    /// <summary>Парковка настроена и включена.</summary>
    public bool Enabled => Slots.Count > 0;

    public ParkingViewModel(ISipService sip) => _sip = sip;

    /// <summary>Перечитать настройки. Значения по умолчанию совпадают с res_parking.conf
    /// нашей АТС; у чужой АТС диапазон другой, поэтому он настраиваемый.</summary>
    public void Reload()
    {
        ParkExtension = Fallback(_sip.GetAppString("park_ext", ""), "500");
        var slots = ParkingSlots.Parse(Fallback(_sip.GetAppString("park_slots", ""), "501-520"));

        // Пересобираем, только если набор реально изменился: строки наблюдаемые, и
        // пересоздание погасило бы занятость слотов до следующего NOTIFY.
        if (slots.Count == Slots.Count && slots.SequenceEqual(Slots.Select(s => s.Slot))) return;

        Slots.Clear();
        foreach (string s in slots) Slots.Add(new ParkingSlotRow(s));
        Recount();
    }

    private static string Fallback(string value, string dflt) =>
        string.IsNullOrWhiteSpace(value) ? dflt : value.Trim();

    /// <summary>Номера, на которые нужна подписка BLF.</summary>
    public IEnumerable<string> Watched => Slots.Select(s => s.Slot);

    /// <summary>Слот сменил состояние (из общей подписки «Event: dialog»).</summary>
    public void SetDialogState(string extension, DialogState state)
    {
        var row = Slots.FirstOrDefault(s => s.Slot == extension);
        if (row == null) return;
        // Unknown — «не знаю», а не «свободен»: гасить плитку по неразобранному NOTIFY
        // значит терять припаркованный звонок из виду.
        if (state == DialogState.Unknown) return;
        bool was = row.Occupied;
        row.Occupied = state is DialogState.Confirmed or DialogState.Early;
        if (!row.Occupied) row.Caller = "";
        else if (!was) ClaimPending(row);
        Recount();
    }

    /// <summary>Кто стоит в слоте, если АТС прислала это в dialog-info.</summary>
    public void SetCaller(string extension, string caller)
    {
        var row = Slots.FirstOrDefault(s => s.Slot == extension);
        if (row != null && caller.Length > 0) row.Caller = caller;
    }

    private void Recount()
    {
        // Порядок слотов сохраняем: перескакивающие плитки — верный способ промахнуться
        // мимо нужной, когда звонок забирают в спешке.
        foreach (var row in Slots)
        {
            bool shown = Occupied.Contains(row);
            if (row.Occupied && !shown)
            {
                int at = 0;
                while (at < Occupied.Count && Slots.IndexOf(Occupied[at]) < Slots.IndexOf(row)) at++;
                Occupied.Insert(at, row);
            }
            else if (!row.Occupied && shown) Occupied.Remove(row);
        }
        OccupiedCount = Occupied.Count;
        AnyOccupied = OccupiedCount > 0;
    }

    // Кого мы сами только что отправили на стоянку. АТС в dialog-info присылает ТОЛЬКО
    // номер слота и «занят» — без звонящего (проверено на живой АТС: тело NOTIFY состоит
    // из entity и state). Но клиент знает, кого паркует он сам, и это ровно тот случай,
    // который оператору важен: «куда я его положил».
    private string _pendingCaller = "";
    private DateTime _pendingAt = DateTime.MinValue;

    /// <summary>Запомнить, кого мы паркуем. Подпись достанется слоту, который займётся
    /// первым в ближайшие секунды. Это догадка, а не факт: если ровно в этот момент паркует
    /// и другой оператор, подпись может достаться его звонку. Поэтому окно короткое, подпись
    /// одноразовая, и ошибка стоит лишь неверной надписи — забрать звонок из слота она не мешает.</summary>
    public void NoteParking(string caller)
    {
        _pendingCaller = caller ?? "";
        _pendingAt = DateTime.UtcNow;
    }

    private void ClaimPending(ParkingSlotRow row)
    {
        if (_pendingCaller.Length == 0) return;
        if (DateTime.UtcNow - _pendingAt > TimeSpan.FromSeconds(6)) { _pendingCaller = ""; return; }
        row.Caller = _pendingCaller;
        _pendingCaller = "";
    }
}
