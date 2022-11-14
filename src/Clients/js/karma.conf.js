// Karma configuration
// Generated on Sun Nov 13 2022 23:02:27 GMT+0100 (Central European Standard Time)

module.exports = function (config) {
    config.set({

        browserify: {
            debug: true,
            transform: [
                [
                    'babelify',
                    {
                        presets: ['es2015']
                    }
                ], [
                    'browserify-istanbul',
                    {
                        instrumenterConfig: {
                            embedSource: true
                        }
                    }
                ]
            ]
        },

        coverageReporter: {
            reporters: [
                { 'type': 'text' },
                { 'type': 'html', dir: 'coverage' },
                { 'type': 'lcov' }
            ]
        },

        // base path that will be used to resolve all patterns (eg. files, exclude)
        basePath: '',


        // frameworks to use
        // available frameworks: https://www.npmjs.com/search?q=keywords:karma-adapter
        frameworks: ['mocha', 'sinon-chai', 'browserify'],


        // list of files / patterns to load in the browser
        files: [
            'dist/test/test/std/**/*.js',
            'dist/test/test/web/**/*.js'
        ],



        // list of files / patterns to exclude
        exclude: [
        ],


        // preprocess matching files before serving them to the browser
        // available preprocessors: https://www.npmjs.com/search?q=keywords:karma-preprocessor
        preprocessors: {
            'dist/test/test/std/**/*.js': ['browserify', 'coverage'],
            'dist/test/test/web/**/*.js': ['browserify', 'coverage'],
        },


        // test results reporter to use
        // possible values: 'dots', 'progress'
        // available reporters: https://www.npmjs.com/search?q=keywords:karma-reporter
        reporters: ['dots', 'progress', 'mocha', 'coverage'],


        // web server port
        port: 9876,


        // enable / disable colors in the output (reporters and logs)
        colors: true,


        // level of logging
        // possible values: config.LOG_DISABLE || config.LOG_ERROR || config.LOG_WARN || config.LOG_INFO || config.LOG_DEBUG
        logLevel: config.LOG_INFO,


        // enable / disable watching file and executing tests whenever any file changes
        autoWatch: true,


        // start these browsers
        // available browser launchers: https://www.npmjs.com/search?q=keywords:karma-launcher
        browsers: ['Chrome'],


        // Continuous Integration mode
        // if true, Karma captures browsers, runs the tests and exits
        singleRun: false,

        // Concurrency level
        // how many browser instances should be started simultaneously
        concurrency: Infinity
    })
}
