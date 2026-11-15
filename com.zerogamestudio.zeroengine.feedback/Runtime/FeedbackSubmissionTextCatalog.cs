using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Feedback
{
    public static partial class FeedbackTextCatalog
    {
        private static readonly Dictionary<SystemLanguage, string[]> SubmissionTexts = new()
        {
            [SystemLanguage.ChineseSimplified] = new[] { "反馈提交中…", "反馈正在重试…", "反馈提交成功，感谢反馈。", "反馈提交失败" },
            [SystemLanguage.ChineseTraditional] = new[] { "回饋提交中…", "回饋正在重試…", "回饋提交成功，感謝回饋。", "回饋提交失敗" },
            [SystemLanguage.English] = new[] { "Submitting feedback…", "Retrying feedback…", "Feedback submitted. Thank you.", "Feedback submission failed" },
            [SystemLanguage.Japanese] = new[] { "フィードバック送信中…", "フィードバックを再試行中…", "フィードバックを送信しました。", "フィードバック送信失敗" },
            [SystemLanguage.Korean] = new[] { "피드백 제출 중…", "피드백 다시 시도 중…", "피드백이 제출되었습니다. 감사합니다.", "피드백 제출 실패" },
            [SystemLanguage.German] = new[] { "Feedback wird gesendet…", "Feedback wird erneut gesendet…", "Feedback gesendet. Vielen Dank.", "Feedback konnte nicht gesendet werden" },
            [SystemLanguage.French] = new[] { "Envoi du retour…", "Nouvel essai d’envoi du retour…", "Retour envoyé. Merci.", "Échec de l’envoi du retour" },
            [SystemLanguage.Spanish] = new[] { "Enviando comentarios…", "Reintentando enviar comentarios…", "Comentarios enviados. Gracias.", "Error al enviar comentarios" },
            [SystemLanguage.Russian] = new[] { "Отправка отзыва…", "Повторная отправка отзыва…", "Отзыв отправлен. Спасибо.", "Не удалось отправить отзыв" },
            [SystemLanguage.Portuguese] = new[] { "Enviando feedback…", "Reenviando feedback…", "Feedback enviado. Obrigado.", "Falha ao enviar feedback" },
            [SystemLanguage.Italian] = new[] { "Invio del feedback…", "Nuovo tentativo di invio del feedback…", "Feedback inviato. Grazie.", "Invio del feedback non riuscito" },
            [SystemLanguage.Dutch] = new[] { "Feedback versturen…", "Feedback opnieuw proberen…", "Feedback verstuurd. Bedankt.", "Feedback versturen mislukt" },
            [SystemLanguage.Polish] = new[] { "Wysyłanie opinii…", "Ponowna próba wysłania opinii…", "Opinia wysłana. Dziękujemy.", "Nie udało się wysłać opinii" }
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
