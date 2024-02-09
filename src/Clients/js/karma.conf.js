const path = require('path');

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
            '**/*.ts': ['karma-typescript', 'coverage'],
        },

        browsers: ['ChromeHeadless'],

        singleRun: true,

        reporters: ['spec', 'coverage', 'junit', 'coverage-istanbul'],

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
                { type: 'lcovonly', dir: 'reports/coverage/web', subdir: 'lcov' },
            ],
        },

        coverageIstanbulReporter: {
            // reports can be any that are listed here: https://github.com/istanbuljs/istanbuljs/tree/73c25ce79f91010d1ff073aa6ff3fd01114f90db/packages/istanbul-reports/lib
            reports: ['json'],

            // base output directory. If you include %browser% in the path it will be replaced with the karma browser name
            dir: path.join(__dirname, 'reports', 'coverage', 'web', 'json'),

            // Combines coverage information from multiple browsers into one report rather than outputting a report
            // for each browser.
            combineBrowserReports: true,

            // if using webpack and pre-loaders, work around webpack breaking the source path
            fixWebpackSourcePaths: false,

            // Omit files with no statements, no functions and no branches covered from the report
            skipFilesWithNoCoverage: false,

            // Most reporters accept additional config options. You can pass these through the `report-config` option
            'report-config': {
                // all options available at: https://github.com/istanbuljs/istanbuljs/blob/73c25ce79f91010d1ff073aa6ff3fd01114f90db/packages/istanbul-reports/lib/html/index.js#L257-L261
                // html: {
                //     // outputs the report in ./coverage/html
                //     subdir: 'html',
                // },
                // json: {
                //     subdir: 'json',
                // },
            },

            // enforce percentage thresholds
            // anything under these percentages will cause karma to fail with an exit code of 1 if not running in watch mode
            thresholds: {
                emitWarning: true, // set to `true` to not fail the test command when thresholds are not met
                // thresholds for all files
                global: {
                    statements: 100,
                    lines: 100,
                    branches: 100,
                    functions: 100,
                },
                // thresholds per file
                each: {
                    statements: 100,
                    lines: 100,
                    branches: 100,
                    functions: 100,
                    overrides: {
                        'baz/component/**/*.js': {
                            statements: 98,
                        },
                    },
                },
            },

            verbose: true, // output config used by istanbul for debugging
        },
    });
};
