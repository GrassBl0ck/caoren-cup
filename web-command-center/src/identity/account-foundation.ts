import crypto from 'node:crypto';

type RandomBytes = (size: number) => Buffer;

const LOGIN_NAME_PATTERN = /^[A-Za-z0-9_]{6,20}$/;
const UPPERCASE = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
const LOWERCASE = 'abcdefghjkmnpqrstuvwxyz';
const DIGITS = '23456789';
const READABLE_ALPHABET = `${UPPERCASE}${LOWERCASE}${DIGITS}`;

const randomIndex = (length: number, randomBytes: RandomBytes): number => {
    const limit = Math.floor(256 / length) * length;
    while (true) {
        const value = randomBytes(1)[0];
        if (value < limit) return value % length;
    }
};
const randomCharacter = (alphabet: string, randomBytes: RandomBytes): string =>
    alphabet[randomIndex(alphabet.length, randomBytes)];

const randomString = (length: number, alphabet: string, randomBytes: RandomBytes): string => {
    let value = '';
    for (let index = 0; index < length; index += 1) value += randomCharacter(alphabet, randomBytes);
    return value;
};

export const validateLoginName = (value: unknown): string => {
    if (typeof value !== 'string' || !LOGIN_NAME_PATTERN.test(value)) throw new Error('login_name_invalid');
    return value;
};

export const generateRandomLoginName = (
    existing: ReadonlySet<string>,
    randomBytes: RandomBytes = crypto.randomBytes,
): string => {
    for (let attempt = 0; attempt < 1_000; attempt += 1) {
        const candidate = `cc_${randomString(8, READABLE_ALPHABET, randomBytes)}`;
        if (!existing.has(candidate)) return candidate;
    }
    throw new Error('login_name_generation_exhausted');
};

export const generateRandomInitialPassword = (randomBytes: RandomBytes = crypto.randomBytes): string => {
    const characters = [
        randomCharacter(UPPERCASE, randomBytes),
        randomCharacter(LOWERCASE, randomBytes),
        randomCharacter(DIGITS, randomBytes),
        ...randomString(11, READABLE_ALPHABET, randomBytes),
    ];
    for (let index = characters.length - 1; index > 0; index -= 1) {
        const target = randomIndex(index + 1, randomBytes);
        [characters[index], characters[target]] = [characters[target], characters[index]];
    }
    return characters.join('');
};
