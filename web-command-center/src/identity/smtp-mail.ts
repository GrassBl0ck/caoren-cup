import nodemailer from 'nodemailer';
import { normalizeEmail } from './email-address';
import { VerificationEmail } from './email-codes';

export function smtpDelivery(env: NodeJS.ProcessEnv = process.env): ((message: VerificationEmail) => Promise<void>) | undefined {
    const { SMTP_HOST: host, SMTP_USER: user, SMTP_PASSWORD: pass } = env;
    if (!host || !user || !pass) return undefined;
    const from = normalizeEmail(env.SMTP_FROM || user);
    const port = Number(env.SMTP_PORT || 465);
    if (!Number.isInteger(port) || port < 1 || port > 65535 || /[\r\n]/.test(host)) throw new Error('smtp_configuration_invalid');
    const secure = env.SMTP_SECURE !== 'false';
    const transport = nodemailer.createTransport({
        host, port, secure, requireTLS: !secure, auth: { user, pass },
        tls: { minVersion: 'TLSv1.2', rejectUnauthorized: true },
        connectionTimeout: 15_000, greetingTimeout: 10_000, socketTimeout: 20_000,
        disableFileAccess: true, disableUrlAccess: true, logger: false, debug: false,
    });
    const labels = { register: '注册账号', 'bind-email': '绑定邮箱', 'reset-password': '重设密码' } as const;
    return async (message) => {
        const info = await transport.sendMail({
            from: { name: '[草人杯]', address: from }, to: message.email,
            subject: `[草人杯]${labels[message.purpose]}验证码`,
            text: `[草人杯]你正在${labels[message.purpose]}。验证码：${message.code}\n${message.expiresInMinutes} 分钟内有效，仅能使用一次。\n请勿将验证码提供给他人。如果不是你本人操作，请忽略此邮件。`,
        });
        if (!info.accepted?.length || info.rejected?.length) throw new Error('smtp_recipient_rejected');
    };
}
