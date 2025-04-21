using System;
using System.Runtime.InteropServices;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace FilePolling
{
    public class OutlookMailSender
    {
        /// <summary>
        /// Отправляет письмо через Microsoft Outlook
        /// </summary>
        /// <param name="to">Получатель (разделители: , или ;)</param>
        /// <param name="subject">Тема письма</param>
        /// <param name="body">Тело письма (HTML или plain text)</param>
        /// <param name="attachments">Пути к файлам вложений</param>
        /// <param name="useHtml">True для HTML-формата тела</param>
        /// <param name="accountName">Имя учётной записи Outlook (null для учётки по умолчанию)</param>
        public static void SendEmail(
            string to,
            string subject,
            string body,
            string[] attachments = null,
            bool useHtml = false,
            string accountName = null)
        {
            Outlook.Application outlookApp = null;
            Outlook.MailItem mailItem = null;

            try
            {
                // Инициализация Outlook
                outlookApp = new Outlook.Application();
                mailItem = (Outlook.MailItem)outlookApp.CreateItem(Outlook.OlItemType.olMailItem);

                // Установка получателей
                mailItem.To = to;

                // Установка темы и тела
                mailItem.Subject = subject;

                if (useHtml)
                    mailItem.HTMLBody = body;
                else
                    mailItem.Body = body;

                // Добавление вложений
                if (attachments != null)
                {
                    foreach (var filePath in attachments)
                    {
                        if (System.IO.File.Exists(filePath))
                            mailItem.Attachments.Add(filePath);
                    }
                }

                // Выбор учётной записи (если указана)
                if (!string.IsNullOrEmpty(accountName))
                {
                    foreach (Outlook.Account account in outlookApp.Session.Accounts)
                    {
                        if (account.DisplayName.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                        {
                            mailItem.SendUsingAccount = account;
                            break;
                        }
                    }
                }

                // Отправка
                mailItem.Send();
            }
            catch (COMException ex)
            {
                // Специфичная обработка ошибок Outlook
                throw new ApplicationException($"Ошибка Outlook: {ex.Message}. HRESULT: {ex.ErrorCode}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Ошибка при отправке письма", ex);
            }
            finally
            {
                // Освобождение ресурсов
                if (mailItem != null) Marshal.ReleaseComObject(mailItem);
                if (outlookApp != null) Marshal.ReleaseComObject(outlookApp);
            }
        }
    }
}
