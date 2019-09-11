module.exports = {
    verbose: true,
    globals: {
        'ts-jest': {
            tsConfig: {
                'target': 'es6',
                'experimentalDecorators': true,
                'esModuleInterop': true
            }
        }
    },
    coveragePathIgnorePatterns: ['<rootDir>/projects/(?:.+?)/dist/'],
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
        '<rootDir>/projects/@uipath-ipc',
        '<rootDir>/projects/@uipath-ipc-sample-app',
        '<rootDir>/projects/@uipath-robot-client',
    ],
    transform: {
        '^.+\\.tsx?$': 'ts-jest',
    },
    testRegex: '(/__tests__/.*|(\\.|/)(test|spec))\\.tsx?$',
    moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json', 'node'],
};
