import {
    copyFileSync,
    ensureDirSync,
    lstatSync,
    readdirSync,
    readJsonSync,
    writeJsonSync,
} from 'fs-extra';
import { join } from 'path';
import { v4 as uuid } from 'uuid';

const COVERAGE_DIR = './coverage';
const NYC_OUTPUT_ALL_DIR = `./.nyc_output/all`;

const COVERAGE_FILENAME = `coverage-final.json`;

// extract all outputs
readdirSync(COVERAGE_DIR)
    .map(name => ({
        path: join(COVERAGE_DIR, name),
        name,
    }))
    .filter(meta => lstatSync(meta.path).isDirectory())
    .forEach(meta => {
        const coveragePath = join(meta.path, COVERAGE_FILENAME);
        const newPath = join(NYC_OUTPUT_ALL_DIR, `${uuid()}.json`);
        ensureDirSync(NYC_OUTPUT_ALL_DIR);
        copyFileSync(coveragePath, newPath);
    });

// normalize data
readdirSync(NYC_OUTPUT_ALL_DIR)
    .map(name => join(NYC_OUTPUT_ALL_DIR, name))
    .forEach(path => {
        const record = readJsonSync(path) as Record<string, any>;
        const normalizedRecord = Object
            .keys(record)
            .reduce((cleaned, key) => {
                const entry = record[key];
                if (entry.data) {
                    cleaned[key] = entry.data;
                } else {
                    cleaned[key] = entry;
                }

                return cleaned;
            }, {} as Record<string, any>);

        writeJsonSync(path, normalizedRecord, { spaces: 4 });
    });
