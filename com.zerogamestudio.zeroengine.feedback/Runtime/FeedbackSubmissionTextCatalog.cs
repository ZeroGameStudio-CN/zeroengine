using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Feedback
{
    public static partial class FeedbackTextCatalog
    {
        private static readonly Dictionary<SystemLanguage, string[]> SubmissionTexts = new()
        {
            [SystemLanguage.ChineseSimplified] = new[] { "提交中…", "自动重试中…", "提交成功，感谢反馈。", "提交失败" },
            [SystemLanguage.ChineseTraditional] = new[] { "提交中…", "自動重試中…", "提交成功，感謝回饋。", "提交失敗" },
            [SystemLanguage.English] = new[] { "Submitting…", "Retrying automatically…", "Submitted. Thanks for your feedback.", "Submission failed" },
            [SystemLanguage.Japanese] = new[] { "送信中…", "自動再試行中…", "送信しました。ご報告ありがとうございます。", "送信失敗" },
            [SystemLanguage.Korean] = new[] { "제출 중…", "자동 재시도 중…", "제출되었습니다. 제보 감사합니다.", "제출 실패" },
            [SystemLanguage.German] = new[] { "Wird gesendet…", "Automatischer Neuversuch…", "Gesendet. Danke für dein Feedback.", "Senden fehlgeschlagen" },
            [SystemLanguage.French] = new[] { "Envoi en cours…", "Nouvel essai automatique…", "Envoyé. Merci pour votre retour.", "Échec de l’envoi" },
            [SystemLanguage.Spanish] = new[] { "Enviando…", "Reintentando automáticamente…", "Enviado. Gracias por tus comentarios.", "Error al enviar" },
            [SystemLanguage.Russian] = new[] { "Отправка…", "Автоматическая повторная попытка…", "Отправлено. Спасибо за отзыв.", "Не удалось отправить" },
            [SystemLanguage.Portuguese] = new[] { "Enviando…", "Tentando novamente…", "Enviado. Obrigado pelo feedback.", "Falha no envio" },
            [SystemLanguage.Italian] = new[] { "Invio…", "Nuovo tentativo automatico…", "Inviato. Grazie per il feedback.", "Invio non riuscito" },
            [SystemLanguage.Dutch] = new[] { "Versturen…", "Automatisch opnieuw proberen…", "Verstuurd. Bedankt voor je feedback.", "Versturen mislukt" },
            [SystemLanguage.Polish] = new[] { "Wysyłanie…", "Automatyczna ponowna próba…", "Wysłano. Dziękujemy za opinię.", "Wysyłanie nie powiodło się" }
        };

        public static string ResolveSubmissionStatus(FeedbackTextId id, string languageCode) =>
            ResolveSubmissionStatus(id, ParseLanguage(languageCode));

        private static string ResolveSubmissionStatus(FeedbackTextId id, SystemLanguage language)
        {
            if (language == SystemLanguage.Chinese) language = SystemLanguage.ChineseSimplified;
            if (!SubmissionTexts.TryGetValue(language, out string[] values)) values = SubmissionTexts[SystemLanguage.English];
            switch (id)
            {
                case FeedbackTextId.Uploading: return values[0];
                case FeedbackTextId.Retrying: return values[1];
                case FeedbackTextId.Uploaded: return values[2];
                case FeedbackTextId.UploadFailed: return values[3];
                default: return null;
            }
        }
    }
}
