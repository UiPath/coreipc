import Jasmine from 'jasmine';
import JCR from 'jasmine-console-reporter';
import { JUnitXmlReporter,  } from 'jasmine-reporters';

async function main() {
    const jasmine = new Jasmine();

    jasmine.loadConfigFile('jasmine.node.json');

    jasmine.env.clearReporters();
    // jasmine.addReporter(
    //     new JCR({
    //         colors: true,
    //         cleanStack: true,
    //         listStyle: 'indent',
    //         timeUnit: 'ms',
    //         emoji: true,
    //         activity: true,
    //     }),
    // );

    jasmine.addReporter(
        new JUnitXmlReporter({
            savePath: './reports/test/node',
            consolidateAll: true,
        }),
    );

    jasmine.exitOnCompletion = true;
    await jasmine.execute();
}

main();
