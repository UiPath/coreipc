module.exports = {
    coveragePathIgnorePatterns: ['<rootDir>/projects/(?:.+?)/dist/'],
    // cacheDirectory: '.jest-cache',
    // coverageDirectory: '.jest-coverage',
    // coverageReporters: ['html'],
    coverageThreshold: {
        global: {
            branches: 30,
            functions: 30,
            lines: 30,
            statements: 30
        }
    },
    collectCoverage: true,
    testPathIgnorePatterns: ['<rootDir>/projects/(?:.+?)/dist/'],
    roots: [
        '<rootDir>/projects/ipc-helpers',
        '<rootDir>/projects/ipc'
    ],
    transform: {
        '^.+\\.tsx?$': 'ts-jest',
    },
    testRegex: '(/__tests__/.*|(\\.|/)(test|spec))\\.tsx?$',
    moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json', 'node'],
};