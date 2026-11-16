import * as fs from 'fs';
import * as path from 'path';
import { expect } from 'chai';
import { NamedPipePath } from '../../src/node';

interface Vector {
    name: string;
    pipeName: string;
    rootEnv: string | null;
    defaultRoot: string;
    expected: string;
}

// Shared with the C# and Python suites: the three implementations must give byte-identical paths.
const vectorsFile = path.join(__dirname, '..', '..', '..', '..', 'MacPipePathVectors.json');
const vectors: Vector[] = JSON.parse(fs.readFileSync(vectorsFile, 'utf8')).vectors;

describe(`${NamedPipePath.resolveMac.name}'s`, () => {
    const utf8Length = (value: string) => Buffer.byteLength(value, 'utf8');

    for (const vector of vectors) {
        it(`should match the shared vector: ${vector.name}`, () => {
            const actual = NamedPipePath.resolveMac(vector.pipeName, vector.rootEnv ?? undefined, vector.defaultRoot);
            expect(actual).to.equal(vector.expected);
        });
    }

    it('should be checked against vectors that cover the whole rule', () => {
        expect(vectors.length).to.be.at.least(8);
        expect(vectors.some((v) => v.pipeName.startsWith('/')), 'rooted passthrough').to.be.true;
        expect(vectors.some((v) => !!v.rootEnv), 'env override').to.be.true;
        expect(vectors.some((v) => v.rootEnv === null), 'default root').to.be.true;
        expect(vectors.some((v) => utf8Length(v.defaultRoot + v.pipeName) > NamedPipePath.maxMacPathBytes), 'truncation').to.be.true;
        expect(vectors.some((v) => utf8Length(v.pipeName) !== v.pipeName.length), 'multi-byte truncation').to.be.true;
    });

    it('should produce paths that fit sun_path and carry no CoreFxPipe_ prefix', () => {
        for (const vector of vectors) {
            expect(utf8Length(vector.expected), vector.name).to.be.at.most(NamedPipePath.maxMacPathBytes);
            expect(vector.expected, vector.name).to.not.contain('CoreFxPipe_');
        }
    });
});

describe(`${NamedPipePath.truncateUtf8.name}'s`, () => {
    it('should keep short values as they are and never split a character', () => {
        expect(NamedPipePath.truncateUtf8('abc', 3)).to.equal('abc');
        expect(NamedPipePath.truncateUtf8('abcd', 3)).to.equal('abc');
        expect(NamedPipePath.truncateUtf8('aé', 2)).to.equal('a'); // é is 2 bytes
        expect(NamedPipePath.truncateUtf8('aé', 3)).to.equal('aé');
        expect(NamedPipePath.truncateUtf8('a🚀', 4)).to.equal('a'); // 🚀 is 4 bytes
        expect(NamedPipePath.truncateUtf8('a🚀', 5)).to.equal('a🚀');
    });
});

describe(`${NamedPipePath.defaultMacPipeRoot.name}'s`, () => {
    const original = process.env.TMPDIR;
    afterEach(() => {
        if (original === undefined) {
            delete process.env.TMPDIR;
        } else {
            process.env.TMPDIR = original;
        }
    });

    it('should be TMPDIR when set', () => {
        process.env.TMPDIR = '/var/folders/zz/zyxvpxvq6csfxvn_n0000000000000/T/';
        expect(NamedPipePath.defaultMacPipeRoot()).to.equal('/var/folders/zz/zyxvpxvq6csfxvn_n0000000000000/T/');
    });

    it('should fall back to /tmp/ when TMPDIR is unset or empty', () => {
        delete process.env.TMPDIR;
        expect(NamedPipePath.defaultMacPipeRoot()).to.equal('/tmp/');
        process.env.TMPDIR = '';
        expect(NamedPipePath.defaultMacPipeRoot()).to.equal('/tmp/');
    });
});
