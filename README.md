[![Build Status](https://travis-ci.org/a-luna/async-file-server.svg?branch=master)](https://travis-ci.org/a-luna/async-file-server)
[![Build status](https://ci.appveyor.com/api/projects/status/db1keyhr337qqh0m?svg=true)](https://ci.appveyor.com/project/a-luna/async-file-server/branch/master)
[![Coverage Status](https://coveralls.io/repos/github/a-luna/async-file-server/badge.svg?branch=master)](https://coveralls.io/github/a-luna/async-file-server?branch=master)
# Async File Server
Light-weight, cross-platform (NET Core 2.1) C# Asynchronous file server and text messaging platform. Utilizes custom extension methods which wrap asynchronous TCP socket method pairs (BeginProcess/EndProcess) within a single method which returns a Task object, providing the benefits of the Task Parallel Library (TPL) to socket programming.
