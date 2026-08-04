using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Feedback
{
    internal static class FeedbackTextCatalog
    {
        private static readonly Dictionary<SystemLanguage, string[]> Texts = new()
        {
            [SystemLanguage.English] = Values("Feedback", "Describe the problem", "Contact (optional)", "Attach image (optional)", "Send", "Cancel", "Uploading", "Uploaded", "Upload failed. Try again."),
            [SystemLanguage.ChineseSimplified] = Values("问题反馈", "请描述问题", "联系方式（选填）", "添加图片（选填）", "发送", "取消", "上传中", "上传成功", "上传失败，请重试"),
            [SystemLanguage.ChineseTraditional] = Values("問題回報", "請描述問題", "聯絡方式（選填）", "新增圖片（選填）", "傳送", "取消", "上傳中", "上傳成功", "上傳失敗，請重試"),
            [SystemLanguage.Japanese] = Values("フィードバック", "問題を入力", "連絡先（任意）", "画像を追加（任意）", "送信", "キャンセル", "送信中", "送信完了", "送信失敗。再試行してください"),
            [SystemLanguage.Korean] = Values("피드백", "문제를 설명해 주세요", "연락처 (선택)", "이미지 추가 (선택)", "보내기", "취소", "업로드 중", "업로드 완료", "업로드 실패. 다시 시도하세요"),
            [SystemLanguage.German] = Values("Feedback", "Problem beschreiben", "Kontakt (optional)", "Bild anhängen (optional)", "Senden", "Abbrechen", "Wird hochgeladen", "Hochgeladen", "Upload fehlgeschlagen. Erneut versuchen."),
            [SystemLanguage.French] = Values("Retour", "Décrivez le problème", "Contact (facultatif)", "Joindre une image (facultatif)", "Envoyer", "Annuler", "Envoi en cours", "Envoyé", "Échec de l’envoi. Réessayez."),
            [SystemLanguage.Spanish] = Values("Comentarios", "Describe el problema", "Contacto (opcional)", "Adjuntar imagen (opcional)", "Enviar", "Cancelar", "Subiendo", "Subido", "Error al subir. Inténtalo de nuevo."),
            [SystemLanguage.Russian] = Values("Отзыв", "Опишите проблему", "Контакт (необязательно)", "Добавить изображение", "Отправить", "Отмена", "Загрузка", "Загружено", "Ошибка загрузки. Повторите."),
            [SystemLanguage.Portuguese] = Values("Feedback", "Descreva o problema", "Contato (opcional)", "Anexar imagem (opcional)", "Enviar", "Cancelar", "Enviando", "Enviado", "Falha no envio. Tente novamente."),
            [SystemLanguage.Italian] = Values("Feedback", "Descrivi il problema", "Contatto (facoltativo)", "Allega immagine (facoltativo)", "Invia", "Annulla", "Caricamento", "Caricato", "Caricamento non riuscito. Riprova."),
            [SystemLanguage.Dutch] = Values("Feedback", "Beschrijf het probleem", "Contact (optioneel)", "Afbeelding toevoegen", "Versturen", "Annuleren", "Uploaden", "Geüpload", "Upload mislukt. Probeer opnieuw."),
            [SystemLanguage.Polish] = Values("Opinia", "Opisz problem", "Kontakt (opcjonalnie)", "Dodaj obraz (opcjonalnie)", "Wyślij", "Anuluj", "Przesyłanie", "Przesłano", "Błąd przesyłania. Spróbuj ponownie.")
        };

        internal static string Resolve(FeedbackTextId id, IFeedbackTextResolver resolver = null)
        {
            if (resolver != null)
            {
                string resolved = resolver.Resolve(id);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }

            return Resolve(id, Application.systemLanguage);
        }

        internal static string Resolve(FeedbackTextId id, SystemLanguage language)
        {
            if (language == SystemLanguage.Chinese)
                language = SystemLanguage.ChineseSimplified;

            if (!Texts.TryGetValue(language, out string[] values))
                values = Texts[SystemLanguage.English];

            int index = (int)id;
            return index >= 0 && index < values.Length ? values[index] : id.ToString();
        }

        internal static int LocaleCount => Texts.Count;

        private static string[] Values(
            string title,
            string description,
            string contact,
            string attachment,
            string send,
            string cancel,
            string uploading,
            string uploaded,
            string uploadFailed)
        {
            return new[]
            {
                title,
                description,
                contact,
                attachment,
                send,
                cancel,
                uploading,
                uploaded,
                uploadFailed
            };
        }
    }
}
