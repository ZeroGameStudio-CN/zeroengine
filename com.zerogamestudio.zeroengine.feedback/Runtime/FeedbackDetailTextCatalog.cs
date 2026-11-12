using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Feedback
{
    public enum FeedbackDetailTextId
    {
        Recent, Empty, CopyNumber, Retry, Close, Pending, Stopped,
        Saved, Partial, Connection, Storage, Server, File, TooLarge, QueueFull, RecordsUnavailable
    }

    public static partial class FeedbackTextCatalog
    {
        private static readonly Dictionary<SystemLanguage, string[]> DetailTexts = new()
        {
            [SystemLanguage.ChineseSimplified] = new[] { "最近提交", "暂无提交记录", "复制编号", "重试", "关闭", "结果待确认", "已停止自动重试，提交结果尚未确认。", "内容已保存在本机，无需重复提交。", "部分附件未送达。", "请检查网络或设备时间。", "请检查存储空间后重试。", "提交服务暂不可用，请稍后重试。", "反馈文件无法读取。", "反馈文件过大，请减少附件后重试。" },
            [SystemLanguage.ChineseTraditional] = new[] { "最近提交", "暫無提交紀錄", "複製編號", "重試", "關閉", "結果待確認", "已停止自動重試，提交結果尚未確認。", "內容已儲存在本機，無需重複提交。", "部分附件未送達。", "請檢查網路或裝置時間。", "請檢查儲存空間後重試。", "提交服務暫不可用，請稍後重試。", "無法讀取回饋檔案。", "回饋檔案過大，請減少附件後重試。" },
            [SystemLanguage.English] = new[] { "Recent submissions", "No submissions yet", "Copy ID", "Retry", "Close", "Awaiting confirmation", "Automatic retries stopped. Delivery is unconfirmed.", "Saved on this device. No need to submit again.", "Some attachments were not received.", "Check your network or device time.", "Check available storage, then retry.", "Submission service unavailable. Try again later.", "The feedback file cannot be read.", "The feedback file is too large. Reduce attachments." },
            [SystemLanguage.Japanese] = new[] { "最近の送信", "送信履歴はありません", "番号をコピー", "再試行", "閉じる", "確認待ち", "自動再試行を停止しました。受信は未確認です。", "端末に保存済みです。再送信は不要です。", "一部の添付ファイルが届いていません。", "ネット接続や端末の時刻をご確認ください。", "空き容量を確認して再試行してください。", "送信サービスを利用できません。後でお試しください。", "報告ファイルを読み込めません。", "報告ファイルが大きすぎます。添付を減らしてください。" },
            [SystemLanguage.Korean] = new[] { "최근 제출", "제출 기록이 없습니다", "번호 복사", "재시도", "닫기", "확인 대기", "자동 재시도가 중단되었습니다. 수신 여부는 미확인입니다.", "이 기기에 저장되었습니다. 다시 제출하지 않아도 됩니다.", "일부 첨부 파일이 전달되지 않았습니다.", "네트워크나 기기 시간을 확인해 주세요.", "저장 공간을 확인한 후 다시 시도해 주세요.", "제출 서비스를 이용할 수 없습니다. 나중에 다시 시도해 주세요.", "제보 파일을 읽을 수 없습니다.", "제보 파일이 너무 큽니다. 첨부 파일을 줄여 주세요." },
            [SystemLanguage.German] = new[] { "Letzte Meldungen", "Noch keine Meldungen", "ID kopieren", "Erneut versuchen", "Schließen", "Bestätigung ausstehend", "Automatische Versuche beendet. Empfang unbestätigt.", "Auf diesem Gerät gespeichert. Nicht erneut senden.", "Einige Anhänge wurden nicht empfangen.", "Netzwerk oder Gerätezeit prüfen.", "Freien Speicher prüfen und erneut versuchen.", "Sendedienst nicht verfügbar. Später versuchen.", "Die Meldungsdatei ist nicht lesbar.", "Die Meldung ist zu groß. Anhänge reduzieren." },
            [SystemLanguage.French] = new[] { "Envois récents", "Aucun envoi", "Copier l’identifiant", "Réessayer", "Fermer", "Confirmation en attente", "Les essais automatiques sont arrêtés. Réception non confirmée.", "Enregistré sur cet appareil. Inutile de renvoyer.", "Certaines pièces jointes n’ont pas été reçues.", "Vérifiez le réseau ou l’heure de l’appareil.", "Vérifiez l’espace disponible, puis réessayez.", "Service d’envoi indisponible. Réessayez plus tard.", "Impossible de lire le fichier du retour.", "Le retour est trop volumineux. Réduisez les pièces jointes." },
            [SystemLanguage.Spanish] = new[] { "Envíos recientes", "Aún no hay envíos", "Copiar ID", "Reintentar", "Cerrar", "Esperando confirmación", "Se detuvieron los reintentos. La recepción no está confirmada.", "Guardado en este dispositivo. No hace falta reenviar.", "No se recibieron algunos archivos adjuntos.", "Comprueba la red o la hora del dispositivo.", "Comprueba el espacio disponible y reintenta.", "Servicio de envío no disponible. Inténtalo más tarde.", "No se puede leer el archivo del informe.", "El informe es demasiado grande. Reduce los adjuntos." },
            [SystemLanguage.Russian] = new[] { "Последние отправки", "Отправок пока нет", "Копировать ID", "Повторить", "Закрыть", "Ожидание подтверждения", "Автоповторы остановлены. Получение не подтверждено.", "Сохранено на устройстве. Повторная отправка не нужна.", "Некоторые вложения не получены.", "Проверьте сеть или время на устройстве.", "Проверьте свободное место и повторите попытку.", "Сервис отправки недоступен. Попробуйте позже.", "Не удаётся прочитать файл отчёта.", "Отчёт слишком большой. Уменьшите вложения." },
            [SystemLanguage.Portuguese] = new[] { "Envios recentes", "Ainda não há envios", "Copiar ID", "Tentar novamente", "Fechar", "Aguardando confirmação", "As tentativas automáticas pararam. Recebimento não confirmado.", "Salvo neste dispositivo. Não é preciso reenviar.", "Alguns anexos não foram recebidos.", "Verifique a rede ou a hora do dispositivo.", "Verifique o espaço disponível e tente novamente.", "Serviço de envio indisponível. Tente mais tarde.", "Não foi possível ler o arquivo do relato.", "O relato é muito grande. Reduza os anexos." },
            [SystemLanguage.Italian] = new[] { "Invii recenti", "Nessun invio", "Copia ID", "Riprova", "Chiudi", "In attesa di conferma", "Tentativi automatici interrotti. Ricezione non confermata.", "Salvato sul dispositivo. Non occorre inviare di nuovo.", "Alcuni allegati non sono stati ricevuti.", "Controlla la rete o l’ora del dispositivo.", "Controlla lo spazio disponibile e riprova.", "Servizio di invio non disponibile. Riprova più tardi.", "Impossibile leggere il file della segnalazione.", "La segnalazione è troppo grande. Riduci gli allegati." },
            [SystemLanguage.Dutch] = new[] { "Recente meldingen", "Nog geen meldingen", "ID kopiëren", "Opnieuw proberen", "Sluiten", "Wacht op bevestiging", "Automatisch proberen gestopt. Ontvangst niet bevestigd.", "Op dit apparaat opgeslagen. Opnieuw indienen is niet nodig.", "Sommige bijlagen zijn niet ontvangen.", "Controleer je netwerk of apparaattijd.", "Controleer vrije opslag en probeer opnieuw.", "Verstuurservice niet beschikbaar. Probeer later opnieuw.", "Het meldingsbestand kan niet worden gelezen.", "De melding is te groot. Verminder het aantal bijlagen." },
            [SystemLanguage.Polish] = new[] { "Ostatnie zgłoszenia", "Brak zgłoszeń", "Kopiuj ID", "Ponów", "Zamknij", "Oczekiwanie na potwierdzenie", "Automatyczne próby zatrzymane. Odbiór niepotwierdzony.", "Zapisano na urządzeniu. Nie trzeba wysyłać ponownie.", "Niektóre załączniki nie dotarły.", "Sprawdź sieć lub czas urządzenia.", "Sprawdź wolne miejsce i spróbuj ponownie.", "Usługa wysyłania niedostępna. Spróbuj później.", "Nie można odczytać pliku zgłoszenia.", "Zgłoszenie jest za duże. Zmniejsz liczbę załączników." }
        };

        private static readonly Dictionary<SystemLanguage, string[]> RecoveryTexts = new()
        {
            [SystemLanguage.ChineseSimplified] = new[] { "待提交反馈较多，请稍后重试。", "提交记录暂不可读取，请稍后重试。" },
            [SystemLanguage.ChineseTraditional] = new[] { "待提交回饋較多，請稍後重試。", "暫時無法讀取提交紀錄，請稍後重試。" },
            [SystemLanguage.English] = new[] { "Too many pending submissions. Try again later.", "Submission records are unreadable. Try again later." },
            [SystemLanguage.Japanese] = new[] { "送信待ちが多いため、後でお試しください。", "送信履歴を読み込めません。後でお試しください。" },
            [SystemLanguage.Korean] = new[] { "대기 중인 제보가 많습니다. 나중에 다시 시도해 주세요.", "제출 기록을 읽을 수 없습니다. 나중에 다시 시도해 주세요." },
            [SystemLanguage.German] = new[] { "Zu viele ausstehende Meldungen. Später versuchen.", "Meldungsdaten sind nicht lesbar. Später versuchen." },
            [SystemLanguage.French] = new[] { "Trop d’envois en attente. Réessayez plus tard.", "Historique illisible. Réessayez plus tard." },
            [SystemLanguage.Spanish] = new[] { "Hay demasiados envíos pendientes. Inténtalo más tarde.", "No se pueden leer los registros. Inténtalo más tarde." },
            [SystemLanguage.Russian] = new[] { "Слишком много ожидающих отправок. Попробуйте позже.", "Не удаётся прочитать записи отправок. Попробуйте позже." },
            [SystemLanguage.Portuguese] = new[] { "Há muitos envios pendentes. Tente mais tarde.", "Não foi possível ler os registros. Tente mais tarde." },
            [SystemLanguage.Italian] = new[] { "Troppi invii in attesa. Riprova più tardi.", "Impossibile leggere lo storico. Riprova più tardi." },
            [SystemLanguage.Dutch] = new[] { "Te veel wachtende meldingen. Probeer later opnieuw.", "Meldingsgegevens niet leesbaar. Probeer later opnieuw." },
            [SystemLanguage.Polish] = new[] { "Za dużo oczekujących zgłoszeń. Spróbuj później.", "Nie można odczytać historii zgłoszeń. Spróbuj później." }
        };

        public static string ResolveDetail(FeedbackDetailTextId id, string languageCode)
        {
            if (id == FeedbackDetailTextId.QueueFull || id == FeedbackDetailTextId.RecordsUnavailable)
            {
                if (!RecoveryTexts.TryGetValue(ParseLanguage(languageCode), out string[] recovery))
                    recovery = RecoveryTexts[SystemLanguage.English];
                return recovery[id == FeedbackDetailTextId.QueueFull ? 0 : 1];
            }
            if (!DetailTexts.TryGetValue(ParseLanguage(languageCode), out string[] values))
                values = DetailTexts[SystemLanguage.English];
            return (int)id >= 0 && (int)id < values.Length ? values[(int)id] : id.ToString();
        }
    }
}
