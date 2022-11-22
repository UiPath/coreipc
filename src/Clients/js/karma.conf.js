module.exports = function (config) {
    config.set({
        client: {
            jasmine: {
                random: false,
            },
        },

        frameworks: ['jasmine', 'karma-typescript'],

        karmaTypescriptConfig: {
            bundlerOptions: {
                entrypoints: /\.test\.ts$/,
                exclude: ['webpack'],
            },
            compilerOptions: {
                esModuleInterop: true,
                target: 'es2017',
                module: 'commonjs',
                moduleResolution: 'node',
                declaration: true,
                resolveJsonModule: true,
                strict: true,
                skipLibCheck: true,
                lib: ['dom', 'es2017'],
            },
            coverageOptions: {
                exclude: /(node_modules|tests|spec)/i,
            },
            // reports: {
            //     lcovonly: {
            //         directory: './coverage',
            //         subdirectory: () => '',
            //         filename: 'lcov.info',
            //     },
            //     html: {
            //         directory: './coverage',
            //         subdirectory: () => '',
            //     },
            // },
            include: [
                './src/**/*.ts',
                './test/infrastructure/**/*.ts',
                './test/std/**/*.ts',
                './test/web/**/*.ts',
            ],
            exclude: ['./node_modules/**/*'],
        },

        files: [
            { pattern: 'src/std/**/*.ts' },
            { pattern: 'src/web/**/*.ts' },
            { pattern: 'test/infrastructure/**/*.ts' },
            { pattern: 'test/std/**/*.ts' },
            { pattern: 'test/web/**/*.ts' },
        ],

        preprocessors: {
            '**/*.ts': ['karma-typescript'],
        },

        browsers: ['ChromeHeadless'],

        singleRun: true,

        // plugins: ['karma-spec-reporter', 'karma-typescript'],

        // reporters: ['dots', 'karma-typescript', 'progress', 'coverage', 'verbose'],
        reporters: ['spec', 'coverage', 'junit'],

        specReporter: {
            maxLogLines: 5,
            suppressSummary: false,
            showSpecTiming: true,
            suppressSkipped: false,
        },

        junitReporter: {
            outputDir: 'reports/test/web',
            outputFile: 'test-results.xml',
            suite: '',
            useBrowserName: false,
            nameFormatter: undefined,
            classNameFormatter: undefined,
            properties: {},
            xmlVersion: null,
        },

        coverageReporter: {
            reporters: [
                { type: 'text' },
                { type: 'html', dir: 'reports/coverage/web', subdir: 'html' },
                { type: 'cobertura', dir: 'reports/coverage/web', subdir: 'cobertura' },
            ],
        },
    });
};
