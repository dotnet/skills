function Invoke-Worker {
    'done'
}

Describe 'Worker' {
    BeforeEach {
        Mock Invoke-Worker { 'done' }
    }

    It 'invokes the worker once' {
        Invoke-Worker

        Should -Invoke Invoke-Worker -Times 1 -Exactly
    }

    It 'waits for completion' {
        Invoke-Worker
        Start-Sleep -Seconds 2

        Should -Invoke Invoke-Worker -Times 1 -Exactly
    }
}
