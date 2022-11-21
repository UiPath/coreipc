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

        // plugins: ['karma-verbose-reporter'],

        reporters: ['dots', 'karma-typescript', 'progress', 'coverage', 'verbose'],

        browsers: ['Chrome'],

        singleRun: true,

        coverageReporter: {
            reporters: [
                { type: 'text' },
                { type: 'html', dir: 'coverage/web', subdir: 'html' },
                { type: 'lcov', dir: 'coverage/web', subdir: 'lcov' },
            ],
        },
    });
};
